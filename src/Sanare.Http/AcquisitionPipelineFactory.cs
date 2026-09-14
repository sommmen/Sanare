using Microsoft.Extensions.Logging;
using Sanare.Core.Acquisition;
using Sanare.Core.Fixtures;
using Sanare.Core.Observability;
using Sanare.Http.Caching;
using Sanare.Http.Identity;
using Sanare.Http.Identity.Compliance;
using Sanare.Http.Politeness;
using Sanare.Http.Resilience;
using Sanare.Http.Robots;

namespace Sanare.Http;

/// <summary>
/// The policy services that must be shared by every acquisition tier for one pipeline.
/// </summary>
/// <remarks>
/// This is deliberately a composition seam rather than a second governance implementation. Browser
/// composition receives these exact instances so its requests consume the same host budget and breaker
/// state as HTTP requests.
/// </remarks>
public sealed record AcquisitionGovernance(
    IHostLimiterRegistry Limiters,
    IRobotsPolicy Robots,
    BlockCircuitBreaker Breaker,
    TimeProvider Clock,
    ScraperMetrics? Metrics);

/// <summary>
/// Assembles a fully governed <see cref="IContentAcquirer"/> from its parts
/// (docs/features/acquisition-pipeline.md, "File Structure").
/// </summary>
/// <remarks>
/// The library ships no DI container registration, so without this factory every consumer would have to
/// rediscover which of the eight collaborators are mandatory, which share state, and which must share a
/// <see cref="TimeProvider"/>. Getting that wrong is silent: a limiter registry built with a different
/// clock than the breaker still compiles and still runs, it just paces incorrectly.
/// </remarks>
public static class AcquisitionPipelineFactory
{
    /// <summary>
    /// Builds a governed acquirer over <paramref name="client"/>, sharing one clock and one limiter
    /// registry across every component that needs them.
    /// </summary>
    /// <param name="client">The HTTP client used for target traffic and robots fetches.</param>
    /// <param name="fixtures">The corpus every response is offered to.</param>
    /// <param name="options">The acquisition policy surface.</param>
    /// <param name="mode">The compliance posture robots decisions are evaluated under.</param>
    /// <param name="compliance">Receives robots decisions for the compliance report.</param>
    /// <param name="metrics">Receives request, delay, block, and cache counters.</param>
    /// <param name="clock">Shared by the limiters, breaker, cache expiry, and retry waits.</param>
    /// <param name="loggerFactory">Supplies the governed acquirer's logger.</param>
    public static IContentAcquirer Create(
        HttpClient client,
        IFixtureCorpus fixtures,
        AcquisitionOptions? options = null,
        AcquisitionMode mode = AcquisitionMode.Compliance,
        ComplianceReporter? compliance = null,
        ScraperMetrics? metrics = null,
        TimeProvider? clock = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(fixtures);

        var resolved = options ?? new AcquisitionOptions();
        var governance = CreateGovernance(client, resolved, metrics, clock);
        return CreateHttpAcquirer(client, fixtures, resolved, mode, compliance, governance, loggerFactory);
    }

    /// <summary>
    /// Builds an HTTP and browser-tier dispatcher that shares one set of governance services across both
    /// transports.
    /// </summary>
    /// <param name="client">The HTTP client used for target traffic and robots fetches.</param>
    /// <param name="fixtures">The corpus every HTTP response is offered to.</param>
    /// <param name="browserFactory">Builds the browser acquirer from the shared governance instances.</param>
    /// <param name="options">The acquisition policy surface.</param>
    /// <param name="mode">The compliance posture robots decisions are evaluated under.</param>
    /// <param name="compliance">Receives robots decisions for the compliance report.</param>
    /// <param name="metrics">Receives request, delay, block, cache, and browser counters.</param>
    /// <param name="clock">Shared by every governed component in both tiers.</param>
    /// <param name="loggerFactory">Supplies the HTTP acquirer's logger.</param>
    /// <remarks>
    /// <paramref name="browserFactory"/> exists to keep this tier-neutral assembly independent from the
    /// Playwright package. It receives the exact limiter registry, robots policy, breaker, clock, and
    /// metrics used by the governed HTTP acquirer; it must compose its browser acquirer from those objects.
    /// </remarks>
    public static IContentAcquirer CreateBrowser(
        HttpClient client,
        IFixtureCorpus fixtures,
        Func<AcquisitionGovernance, IContentAcquirer> browserFactory,
        AcquisitionOptions? options = null,
        AcquisitionMode mode = AcquisitionMode.Compliance,
        ComplianceReporter? compliance = null,
        ScraperMetrics? metrics = null,
        TimeProvider? clock = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(fixtures);
        ArgumentNullException.ThrowIfNull(browserFactory);

        var resolved = options ?? new AcquisitionOptions();
        var governance = CreateGovernance(client, resolved, metrics, clock);
        var browser = browserFactory(governance)
            ?? throw new InvalidOperationException("The browser acquirer factory returned null.");
        var http = CreateHttpAcquirer(client, fixtures, resolved, mode, compliance, governance, loggerFactory);

        return new TieredContentAcquirer(http, browser);
    }

    /// <summary>
    /// Builds the shared policy services (limiter registry, robots policy, circuit breaker, clock) that
    /// every tier of one pipeline must reuse, without also building an HTTP transport.
    /// </summary>
    /// <remarks>
    /// Use this when composing an <see cref="IContentAcquirer"/> for a non-HTTP tier — for example the
    /// browser tier's composition root — so it enforces the same host budget, robots decisions, and
    /// breaker state as the HTTP tier built by <see cref="Create"/> in the same pipeline. Call this once
    /// per pipeline and reuse the returned instances everywhere a tier needs governance.
    /// </remarks>
    /// <param name="client">The HTTP client robots fetches are issued against.</param>
    /// <param name="options">The acquisition policy surface.</param>
    /// <param name="metrics">Receives request, delay, and block counters.</param>
    /// <param name="clock">Shared by the limiters, breaker, and retry waits.</param>
    public static AcquisitionGovernance CreateGovernance(
        HttpClient client,
        AcquisitionOptions? options = null,
        ScraperMetrics? metrics = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var resolved = options ?? new AcquisitionOptions();
        var resolvedClock = clock ?? TimeProvider.System;

        var limiters = new HostLimiterRegistry(resolved, resolvedClock, random: null, metrics);
        var robots = new RobotsPolicy(client, resolved, limiters, resolvedClock);
        var breaker = new BlockCircuitBreaker(resolved, resolvedClock, metrics);

        return new AcquisitionGovernance(limiters, robots, breaker, resolvedClock, metrics);
    }

    private static IContentAcquirer CreateHttpAcquirer(
        HttpClient client,
        IFixtureCorpus fixtures,
        AcquisitionOptions options,
        AcquisitionMode mode,
        ComplianceReporter? compliance,
        AcquisitionGovernance governance,
        ILoggerFactory? loggerFactory)
    {
        var transport = new HttpContentAcquirer(client, fixtures, options, governance.Clock);
        var cache = options.EffectiveCache.Enabled
            ? new FileHttpResponseCache(options.EffectiveCache.Root, governance.Metrics)
            : null;

        return new GovernedContentAcquirer(
            transport,
            governance.Limiters,
            governance.Robots,
            options,
            mode,
            cache,
            breaker: governance.Breaker,
            compliance: compliance,
            metrics: governance.Metrics,
            clock: governance.Clock,
            logger: loggerFactory?.CreateLogger<GovernedContentAcquirer>());
    }
}
