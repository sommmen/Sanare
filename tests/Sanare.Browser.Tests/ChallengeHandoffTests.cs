using Microsoft.Playwright;
using Sanare.Core.Acquisition;
using Sanare.Http.Resilience;
using Xunit;

namespace Sanare.Browser.Tests;

/// <summary>
/// Gating tests for <see cref="PlaywrightChallengeHandoff"/>. The Enabled/ExecutionMode check in
/// <c>OpenAsync</c> runs before any Playwright object is touched (docs/features/browser-tier.md, T13),
/// so these negative-path cases never launch a browser and can use a fake <see cref="IPlaywright"/>
/// whose members throw if ever invoked.
/// </summary>
public sealed class ChallengeHandoffTests
{
    [Fact]
    public async Task OpenAsync_throws_when_browser_tier_is_disabled()
    {
        var handoff = Build(enabled: false, executionMode: ExecutionMode.Live);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handoff.OpenAsync("source-1").AsTask());
    }

    [Theory]
    [InlineData(ExecutionMode.OfflineFixture)]
    [InlineData(ExecutionMode.Unattended)]
    public async Task OpenAsync_throws_when_execution_mode_is_not_live(ExecutionMode executionMode)
    {
        var handoff = Build(enabled: true, executionMode: executionMode);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handoff.OpenAsync("source-1").AsTask());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OpenAsync_rejects_blank_source_id_before_any_gate_or_playwright_use(string? sourceId)
    {
        var handoff = Build(enabled: true, executionMode: ExecutionMode.Live);

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException specifically for null.
        await Assert.ThrowsAnyAsync<ArgumentException>(() => handoff.OpenAsync(sourceId!).AsTask());
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var options = new BrowserOptions(Enabled: true);
        var playwright = new ThrowingPlaywright();
        var contexts = new BrowserContextFactory();
        Func<string, CancellationToken, ValueTask<ChallengeHandoffSession>> resolveSession =
            (_, _) => throw new InvalidOperationException("should not be invoked");
        Func<CancellationToken, ValueTask<ChallengeHandoffResult>> awaitConfirmation =
            _ => throw new InvalidOperationException("should not be invoked");

        Assert.Throws<ArgumentNullException>(() =>
            new PlaywrightChallengeHandoff(null!, contexts, resolveSession, awaitConfirmation, options, ExecutionMode.Live));
        Assert.Throws<ArgumentNullException>(() =>
            new PlaywrightChallengeHandoff(playwright, null!, resolveSession, awaitConfirmation, options, ExecutionMode.Live));
        Assert.Throws<ArgumentNullException>(() =>
            new PlaywrightChallengeHandoff(playwright, contexts, null!, awaitConfirmation, options, ExecutionMode.Live));
        Assert.Throws<ArgumentNullException>(() =>
            new PlaywrightChallengeHandoff(playwright, contexts, resolveSession, null!, options, ExecutionMode.Live));
        Assert.Throws<ArgumentNullException>(() =>
            new PlaywrightChallengeHandoff(playwright, contexts, resolveSession, awaitConfirmation, null!, ExecutionMode.Live));
    }

    private static PlaywrightChallengeHandoff Build(bool enabled, ExecutionMode executionMode) =>
        new(
            new ThrowingPlaywright(),
            new BrowserContextFactory(),
            resolveSession: (_, _) => throw new InvalidOperationException("Gate should reject before session resolution."),
            awaitConfirmation: _ => throw new InvalidOperationException("Gate should reject before confirmation."),
            new BrowserOptions(Enabled: enabled),
            executionMode);

    /// <summary>An <see cref="IPlaywright"/> stand-in that fails the test if any member is ever touched.</summary>
    private sealed class ThrowingPlaywright : IPlaywright
    {
        private static InvalidOperationException NotExpected() =>
            new("Gate should reject before any Playwright member is used.");

        public IAPIRequest APIRequest => throw NotExpected();
        public IReadOnlyDictionary<string, BrowserNewContextOptions> Devices => throw NotExpected();
        public IBrowserType Chromium => throw NotExpected();
        public IBrowserType Firefox => throw NotExpected();
        public IBrowserType Webkit => throw NotExpected();
        public ISelectors Selectors => throw NotExpected();
        public IBrowserType this[string name] => throw NotExpected();

        public void Dispose()
        {
        }
    }
}
