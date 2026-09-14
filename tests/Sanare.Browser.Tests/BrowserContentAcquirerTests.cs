using System.Globalization;
using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Acquisition;
using Sanare.Core.Fixtures;
using Sanare.Http;
using Sanare.Http.Identity;
using Sanare.Http.Politeness;
using Sanare.Http.Resilience;
using Sanare.Http.Robots;
using Xunit;

namespace Sanare.Browser.Tests;

/// <summary>
/// Gating tests for <see cref="BrowserContentAcquirer"/>. <c>BrowserTierGate.EnsureAllowed</c> runs
/// immediately after policy resolution and before the breaker, robots, host limiter, or pool are ever
/// touched (docs/features/browser-tier.md, T11), so the disabled-tier paths below never launch a
/// browser and can use throwing fakes for every downstream collaborator to prove the gate truly comes
/// first.
/// </summary>
public sealed class BrowserContentAcquirerTests
{
    private static readonly Uri TargetUri = new("https://example.com/page");

    [Fact]
    public async Task AcquireAsync_throws_when_browser_tier_is_globally_disabled()
    {
        var acquirer = Build(new AcquisitionOptions(Browser: new BrowserOptions(Enabled: false)));
        var request = new BrowserAcquisitionRequest(
            TargetUri,
            "source-1",
            new AcquisitionSpec(AcquisitionMethod.Get, TargetUri.ToString(), new Dictionary<string, string>(), WaitFor: null, Interactions: []),
            CultureInfo.GetCultureInfo("en-US"),
            NavigationContext.TopLevel);

        var ex = await Assert.ThrowsAsync<AcquisitionException>(() => acquirer.AcquireAsync(request).AsTask());
        Assert.Equal("SNR-BRW-001", ex.Code);
    }

    [Fact]
    public async Task AcquireAsync_throws_when_source_does_not_opt_into_browser_tier()
    {
        // Global tier enabled but no per-source AllowBrowserTier override: still gated.
        var acquirer = Build(new AcquisitionOptions(Browser: new BrowserOptions(Enabled: true)));
        var request = new BrowserAcquisitionRequest(
            TargetUri,
            "source-1",
            new AcquisitionSpec(AcquisitionMethod.Get, TargetUri.ToString(), new Dictionary<string, string>(), WaitFor: null, Interactions: []),
            CultureInfo.GetCultureInfo("en-US"),
            NavigationContext.TopLevel);

        var ex = await Assert.ThrowsAsync<AcquisitionException>(() => acquirer.AcquireAsync(request).AsTask());
        Assert.Equal("SNR-BRW-001", ex.Code);
    }

    [Fact]
    public async Task AcquireAsync_plan_overload_throws_when_acquisition_spec_is_missing()
    {
        // No Playwright/pool interaction is required for this path: the plan's Acquisition is validated
        // before BrowserAcquisitionRequest is even constructed.
        var acquirer = Build(new AcquisitionOptions(Browser: new BrowserOptions(Enabled: true)));
        var request = new AcquisitionRequest(TargetUri, "source-1", Tier: AcquisitionTier.Browser, Acquisition: null);

        var ex = await Assert.ThrowsAsync<AcquisitionException>(() => acquirer.AcquireAsync(request).AsTask());
        Assert.Equal("SNR-BRW-004", ex.Code);
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var pool = new ThrowingBrowserPool();
        var pages = new PageScope();
        var steps = new ThrowingStepExecutor();
        var fixtures = new ThrowingFixtureCorpus();
        var options = new AcquisitionOptions();
        var governance = new AcquisitionGovernance(
            new ThrowingHostLimiterRegistry(),
            new ThrowingRobotsPolicy(),
            new BlockCircuitBreaker(),
            TimeProvider.System,
            Metrics: null);

        Assert.Throws<ArgumentNullException>(() =>
            new BrowserContentAcquirer(null!, pages, steps, fixtures, options, AcquisitionMode.Compliance, governance));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserContentAcquirer(pool, null!, steps, fixtures, options, AcquisitionMode.Compliance, governance));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserContentAcquirer(pool, pages, null!, fixtures, options, AcquisitionMode.Compliance, governance));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserContentAcquirer(pool, pages, steps, null!, options, AcquisitionMode.Compliance, governance));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserContentAcquirer(pool, pages, steps, fixtures, null!, AcquisitionMode.Compliance, governance));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserContentAcquirer(pool, pages, steps, fixtures, options, AcquisitionMode.Compliance, null!));
    }

    private static BrowserContentAcquirer Build(AcquisitionOptions options) =>
        new(
            new ThrowingBrowserPool(),
            new PageScope(),
            new ThrowingStepExecutor(),
            new ThrowingFixtureCorpus(),
            options,
            AcquisitionMode.Compliance,
            new AcquisitionGovernance(
                new ThrowingHostLimiterRegistry(),
                new ThrowingRobotsPolicy(),
                new BlockCircuitBreaker(),
                TimeProvider.System,
                Metrics: null));

    /// <summary>Every member throws: the gate must reject before any of these are ever reached.</summary>
    private sealed class ThrowingBrowserPool : IBrowserPool
    {
        public ValueTask<IBrowserLease> RentAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Pool should not be rented from before the gate.");

        public ValueTask SweepIdleAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Pool should not be swept before the gate.");

        public BrowserPoolStatistics Statistics =>
            throw new InvalidOperationException("Pool statistics should not be read before the gate.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingStepExecutor : IBrowserStepExecutor
    {
        public Task RunAsync(Microsoft.Playwright.IPage page, IReadOnlyList<InteractionStep> steps, BrowserOptions options, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Steps should not run before the gate.");
    }

    private sealed class ThrowingFixtureCorpus : IFixtureCorpus
    {
        public ValueTask<FixtureRecord> CaptureAsync(CaptureRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fixture capture should not occur before the gate.");

        public ValueTask<FixtureContent?> GetContentAsync(string fixtureId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fixture content should not be read before the gate.");

        public ValueTask<IReadOnlyList<FixtureRecord>> QueryAsync(FixtureQuery query, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fixture query should not occur before the gate.");

        public ValueTask<FixtureSlice> SliceAsync(string fixtureId, FixtureSliceRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fixture slice should not occur before the gate.");

        public ValueTask<PruneReport> PruneAsync(IReadOnlyCollection<string> protectedFixtureIds, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fixture prune should not occur before the gate.");
    }

    private sealed class ThrowingHostLimiterRegistry : IHostLimiterRegistry
    {
        public ValueTask<IAsyncDisposable> AcquireAsync(string host, CancellationToken ct = default) =>
            throw new InvalidOperationException("Host limiter should not be acquired before the gate.");

        public HostBudget GetBudget(string host) =>
            throw new InvalidOperationException("Host budget should not be read before the gate.");

        public void ApplyCrawlDelay(string host, TimeSpan? crawlDelay) =>
            throw new InvalidOperationException("Crawl delay should not be applied before the gate.");

        public ValueTask WaitForPolitenessAsync(string host, CancellationToken ct = default) =>
            throw new InvalidOperationException("Politeness wait should not occur before the gate.");
    }

    private sealed class ThrowingRobotsPolicy : IRobotsPolicy
    {
        public ValueTask<RobotsDecision> EvaluateAsync(Uri url, AcquisitionMode mode, CancellationToken ct = default) =>
            throw new InvalidOperationException("Robots evaluation should not occur before the gate.");

        public ValueTask<RobotsRuleSet> GetRuleSetAsync(string host, CancellationToken ct = default) =>
            throw new InvalidOperationException("Robots rule set should not be fetched before the gate.");
    }
}
