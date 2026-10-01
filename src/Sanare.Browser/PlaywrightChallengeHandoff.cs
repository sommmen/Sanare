using System.Globalization;
using Microsoft.Playwright;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity;
using Sanare.Http.Identity.Profiles;
using Sanare.Http.Resilience;

namespace Sanare.Browser;

/// <summary>Details needed to open one operator-supervised browser session.</summary>
public sealed record ChallengeHandoffSession(Uri Url, IdentityProfile IdentityProfile, CultureInfo Culture, string TimezoneId = "UTC");

/// <summary>
/// Opens a visible Chromium session for an operator to complete normal browsing after a challenge pause.
/// </summary>
/// <remarks>
/// This hand-off deliberately bypasses <see cref="IBrowserPool"/>: an operator session is not a bounded,
/// automated browser operation. It performs no interactions, identity rotation, breaker mutation, or probe.
/// </remarks>
public sealed class PlaywrightChallengeHandoff : IChallengeHandoff
{
    private readonly IPlaywright _playwright;
    private readonly BrowserContextFactory _contexts;
    private readonly Func<string, CancellationToken, ValueTask<ChallengeHandoffSession>> _resolveSession;
    private readonly Func<CancellationToken, ValueTask<ChallengeHandoffResult>> _awaitConfirmation;
    private readonly BrowserOptions _options;
    private readonly ExecutionMode _executionMode;

    public PlaywrightChallengeHandoff(
        IPlaywright playwright,
        BrowserContextFactory contexts,
        Func<string, CancellationToken, ValueTask<ChallengeHandoffSession>> resolveSession,
        Func<CancellationToken, ValueTask<ChallengeHandoffResult>> awaitConfirmation,
        BrowserOptions options,
        ExecutionMode executionMode)
    {
        _playwright = playwright ?? throw new ArgumentNullException(nameof(playwright));
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _resolveSession = resolveSession ?? throw new ArgumentNullException(nameof(resolveSession));
        _awaitConfirmation = awaitConfirmation ?? throw new ArgumentNullException(nameof(awaitConfirmation));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _executionMode = executionMode;
    }

    /// <inheritdoc />
    public async ValueTask<ChallengeHandoffResult> OpenAsync(string sourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (!_options.Enabled || _executionMode is not ExecutionMode.Live)
        {
            throw new InvalidOperationException("Challenge hand-off requires Browser.Enabled and live operator execution.");
        }

        var session = await _resolveSession(sourceId, ct).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(session);
        var request = new BrowserAcquisitionRequest(
            session.Url,
            sourceId,
            EmptyAcquisition,
            session.Culture,
            NavigationContext.TopLevel,
            TimezoneId: session.TimezoneId);
        var contextOptions = _contexts.BuildOptions(request, session.IdentityProfile, AcquisitionMode.Compliance);

        await using var browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = false }).ConfigureAwait(false);
        await using var context = await browser.NewContextAsync(contextOptions).ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);
        try
        {
            await page.GotoAsync(session.Url.AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
            return await _awaitConfirmation(ct).ConfigureAwait(false);
        }
        finally
        {
            await page.CloseAsync().ConfigureAwait(false);
        }
    }

    private static readonly Sanare.Abstractions.Plans.AcquisitionSpec EmptyAcquisition = new(
        Sanare.Abstractions.Plans.AcquisitionMethod.Get,
        "https://example.invalid/",
        new Dictionary<string, string>(),
        null,
        Array.Empty<Sanare.Abstractions.Plans.InteractionStep>());
}