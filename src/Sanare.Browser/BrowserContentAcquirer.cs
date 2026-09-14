using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Playwright;
using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Acquisition;
using Sanare.Core.Fixtures;
using Sanare.Core.Observability;
using Sanare.Http;
using Sanare.Http.Identity;
using Sanare.Http.Politeness;
using Sanare.Http.Resilience;
using Sanare.Http.Robots;

namespace Sanare.Browser;

/// <summary>
/// Acquires rendered DOM through the governed browser tier.
/// </summary>
/// <remarks>
/// The browser tier is an explicit, double-opt-in transport. It shares the HTTP tier's robots,
/// host-limiting, and circuit-breaker instances, but never falls back to another tier.
/// </remarks>
public sealed class BrowserContentAcquirer : IContentAcquirer, IBrowserContentAcquirer
{
    private static readonly ActivitySource Activity = new("Sanare");

    private readonly IBrowserPool _pool;
    private readonly PageScope _pages;
    private readonly IBrowserStepExecutor _steps;
    private readonly IFixtureCorpus _fixtures;
    private readonly AcquisitionOptions _options;
    private readonly AcquisitionMode _mode;
    private readonly BrowserTierGate _gate;
    private readonly IHostLimiterRegistry _limiters;
    private readonly IRobotsPolicy _robots;
    private readonly BlockCircuitBreaker _breaker;
    private readonly TimeProvider _clock;
    private readonly ScraperMetrics? _metrics;
    private readonly ChallengeDetector _challenges;
    private readonly CookieBridge? _cookies;
    private readonly NetworkLogRecorder? _networkRecorder;

    public BrowserContentAcquirer(
        IBrowserPool pool,
        PageScope pages,
        IBrowserStepExecutor steps,
        IFixtureCorpus fixtures,
        AcquisitionOptions options,
        AcquisitionMode mode,
        AcquisitionGovernance governance,
        BrowserTierGate? gate = null,
        ChallengeDetector? challenges = null,
        CookieBridge? cookies = null,
        NetworkLogRecorder? networkRecorder = null)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        _fixtures = fixtures ?? throw new ArgumentNullException(nameof(fixtures));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _mode = mode;
        ArgumentNullException.ThrowIfNull(governance);
        _limiters = governance.Limiters;
        _robots = governance.Robots;
        _breaker = governance.Breaker;
        _clock = governance.Clock;
        _metrics = governance.Metrics;
        _gate = gate ?? new BrowserTierGate();
        _challenges = challenges ?? new ChallengeDetector();
        _cookies = cookies;
        _networkRecorder = networkRecorder;
    }

    /// <inheritdoc />
    public ValueTask<AcquiredContent> AcquireAsync(AcquisitionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var acquisition = request.Acquisition ?? throw new AcquisitionException(
            "SNR-BRW-004", "Browser acquisition requires the plan acquisition specification.");

        return AcquireAsync(new BrowserAcquisitionRequest(
            request.Url,
            request.SourceId,
            acquisition,
            CultureInfo.GetCultureInfo("en-US"),
            Sanare.Http.Identity.NavigationContext.TopLevel,
            _options.EffectiveBrowser.CaptureNetwork), request, ct);
    }

    /// <inheritdoc />
    public ValueTask<AcquiredContent> AcquireAsync(BrowserAcquisitionRequest request, CancellationToken cancellationToken = default) =>
        AcquireAsync(request, sourceRequest: null, cancellationToken);

    private async ValueTask<AcquiredContent> AcquireAsync(
        BrowserAcquisitionRequest browserRequest,
        AcquisitionRequest? sourceRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(browserRequest);
        var request = sourceRequest ?? new AcquisitionRequest(
            browserRequest.TargetUri,
            browserRequest.SourceId,
            Tier: AcquisitionTier.Browser,
            Acquisition: browserRequest.Acquisition);
        var policy = _options.ResolveFor(browserRequest.TargetUri.Host, browserRequest.SourceId);

        _gate.EnsureAllowed(policy);
        var host = browserRequest.TargetUri.Host;
        using var span = Activity.StartActivity(SpanNames.BrowserNavigate, ActivityKind.Client);
        span?.SetTag("host", host);
        span?.SetTag("sanare.source_id", browserRequest.SourceId);

        _breaker.ThrowIfOpen(host);
        var robots = await _robots.EvaluateAsync(browserRequest.TargetUri, _mode, cancellationToken).ConfigureAwait(false);
        _limiters.ApplyCrawlDelay(host, robots.CrawlDelay);
        if (!robots.Allowed && _mode == AcquisitionMode.Compliance)
        {
            throw new AcquisitionException("SNR-ACQ-001", $"robots.txt disallows '{browserRequest.TargetUri}'.");
        }

        await using var hostLease = await _limiters.AcquireAsync(host, cancellationToken).ConfigureAwait(false);
        await _limiters.WaitForPolitenessAsync(host, cancellationToken).ConfigureAwait(false);

        var started = _clock.GetTimestamp();
        await using var browserLease = await _pool.RentAsync(cancellationToken).ConfigureAwait(false);
        var rendered = await _pages.RunAsync(browserLease, async page =>
        {
            var wait = WaitStrategyParser.Parse(browserRequest.Acquisition.WaitFor);
            if (_options.EffectiveBrowser.BlockResources && ResourceBlocker.ShouldBlock(wait, policy.RequiresImages))
            {
                await ResourceBlocker.InstallAsync(browserLease.Context, policy.Browser).ConfigureAwait(false);
            }

            if (_cookies is not null)
            {
                await _cookies.SeedAsync(browserLease.Context, host, cancellationToken).ConfigureAwait(false);
            }
            using var network = AttachNetworkRecorder(browserLease.Context, browserRequest);
            var response = await page.GotoAsync(browserRequest.TargetUri.AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
            await wait.ApplyAsync(page, cancellationToken).ConfigureAwait(false);
            await _steps.RunAsync(page, browserRequest.Acquisition.Interactions, policy.Browser, cancellationToken).ConfigureAwait(false);
            if (_cookies is not null)
            {
                await _cookies.HarvestAsync(browserLease.Context, host, cancellationToken).ConfigureAwait(false);
            }

            var html = await page.ContentAsync().ConfigureAwait(false);
            return new RenderedPage(page.Url, response?.Status ?? 0, response?.Headers ?? new Dictionary<string, string>(), html);
        }, cancellationToken).ConfigureAwait(false);

        var body = Encoding.UTF8.GetBytes(rendered.Html);
        var elapsed = _clock.GetElapsedTime(started);
        var content = new AcquiredContent(
            browserRequest.TargetUri,
            new Uri(rendered.FinalUrl),
            rendered.StatusCode,
            "text/html",
            Encoding.UTF8,
            body,
            rendered.Headers,
            ContentOrigin.Browser,
            FixtureId: null,
            elapsed,
            IdentityProfileId: request.Identity?.ProfileId);

        var fixtureId = await CaptureAsync(request, content, cancellationToken).ConfigureAwait(false);
        content = content with { FixtureId = fixtureId };
        RecordOutcome(request, content, span);
        return content;
    }

    private IDisposable? AttachNetworkRecorder(IBrowserContext context, BrowserAcquisitionRequest request)
    {
        if (!request.CaptureNetwork || _networkRecorder is null) return null;
        return _networkRecorder.Attach(context, new BrowserNetworkLog());
    }

    private async ValueTask<string?> CaptureAsync(AcquisitionRequest request, AcquiredContent content, CancellationToken ct)
    {
        await using var stream = new MemoryStream(content.Body.ToArray(), writable: false);
        var fixture = await _fixtures.CaptureAsync(new CaptureRequest(
            content.FinalUrl.AbsoluteUri,
            request.SourceId,
            AcquisitionTier.Browser,
            content.ContentType,
            stream,
            content.Headers,
            request.PageRole), ct).ConfigureAwait(false);
        return fixture.Id;
    }

    private void RecordOutcome(AcquisitionRequest request, AcquiredContent content, Activity? span)
    {
        var host = request.Url.Host;
        var severity = _challenges.Classify(content);
        var blocked = severity != ChallengeSeverity.None || content.StatusCode is 403 or 429;
        _metrics?.RecordRequest(host, $"{content.StatusCode / 100}xx", AcquisitionTier.Browser.ToString());
        span?.SetTag("http.response.status_code", content.StatusCode);

        if (blocked)
        {
            var state = _breaker.RecordBlock(host, severity);
            span?.SetTag("sanare.breaker_state", state.ToString());
            throw state == BreakerState.ChallengePaused
                ? new AcquisitionException("SNR-ACQ-011", $"'{host}' is serving a challenge; acquisition is paused pending an operator hand-off.")
                : new AcquisitionException("SNR-ACQ-003", $"'{host}' refused the request with HTTP {content.StatusCode}.");
        }

        _breaker.RecordSuccess(host);
    }

    private sealed record RenderedPage(string FinalUrl, int StatusCode, IReadOnlyDictionary<string, string> Headers, string Html);
}