using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sanare.Core.Acquisition;
using Sanare.Core.Observability;
using Sanare.Http.Caching;
using Sanare.Http.Identity;
using Sanare.Http.Identity.Compliance;
using Sanare.Http.Politeness;
using Sanare.Http.Resilience;
using Sanare.Http.Robots;

namespace Sanare.Http;

/// <summary>
/// Wraps the Core <see cref="HttpContentAcquirer"/> transport with every governance concern the
/// acquisition pipeline owes a target host (docs/features/acquisition-pipeline.md, "Data Flow").
/// </summary>
/// <remarks>
/// <para>
/// The order of operations is deliberate and is the reason this type exists as a decorator rather than as
/// options on the transport:
/// </para>
/// <list type="number">
///   <item><description>the breaker is consulted first, so a host we already know is refusing us costs no socket at all;</description></item>
///   <item><description>robots is evaluated before the limiter lease, so a disallowed URL never consumes a token;</description></item>
///   <item><description>the limiter lease and politeness gap are taken before the cache lookup is <em>used</em> for a network call, but a fresh cache hit returns before either — a cached page owes the host nothing;</description></item>
///   <item><description>retries happen inside the lease, so a retried request is still paced;</description></item>
///   <item><description>breaker accounting and adaptive feedback see every outcome, including the ones that succeeded.</description></item>
/// </list>
/// <para>
/// Fixture capture happens inside the transport, which means error bodies are captured too. That is
/// intentional: a 403 challenge page is exactly the evidence a later healing run needs, and discarding it
/// because it was not a 200 would throw away the most diagnostic artefact in the run.
/// </para>
/// </remarks>
public sealed class GovernedContentAcquirer : IContentAcquirer
{
    private static readonly ActivitySource Activity = new("Sanare.Acquisition");

    private readonly IContentAcquirer _transport;
    private readonly IHostLimiterRegistry _limiters;
    private readonly IRobotsPolicy _robots;
    private readonly BlockCircuitBreaker _breaker;
    private readonly ChallengeDetector _challenges;
    private readonly AcquisitionResiliencePipeline _resilience;
    private readonly AdaptiveRateController _adaptive;
    private readonly IHttpResponseCache? _cache;
    private readonly CachePolicy _cachePolicy;
    private readonly AcquisitionOptions _options;
    private readonly AcquisitionMode _mode;
    private readonly ComplianceReporter? _compliance;
    private readonly ScraperMetrics? _metrics;
    private readonly TimeProvider _clock;
    private readonly ILogger<GovernedContentAcquirer>? _logger;

    /// <summary>Creates a governed acquirer around <paramref name="transport"/>.</summary>
    /// <param name="transport">The Core transport that performs the actual send and fixture capture.</param>
    /// <param name="limiters">The per-host token buckets and politeness gaps.</param>
    /// <param name="robots">The robots.txt policy consulted before every request.</param>
    /// <param name="options">The acquisition policy surface.</param>
    /// <param name="mode">The compliance posture robots decisions are evaluated under.</param>
    /// <param name="cache">The HTTP response cache, when one is configured.</param>
    /// <param name="breaker">The block circuit breaker. One is created when omitted.</param>
    /// <param name="challenges">The challenge detector. One is created when omitted.</param>
    /// <param name="adaptive">The adaptive rate controller. One is created when omitted.</param>
    /// <param name="compliance">Receives robots decisions for the compliance report.</param>
    /// <param name="metrics">Receives request, delay, and block counters.</param>
    /// <param name="clock">Paces waits and stamps cache entries. Inject a fake in tests.</param>
    /// <param name="logger">Receives governance diagnostics.</param>
    public GovernedContentAcquirer(
        IContentAcquirer transport,
        IHostLimiterRegistry limiters,
        IRobotsPolicy robots,
        AcquisitionOptions? options = null,
        AcquisitionMode mode = AcquisitionMode.Compliance,
        IHttpResponseCache? cache = null,
        BlockCircuitBreaker? breaker = null,
        ChallengeDetector? challenges = null,
        AdaptiveRateController? adaptive = null,
        ComplianceReporter? compliance = null,
        ScraperMetrics? metrics = null,
        TimeProvider? clock = null,
        ILogger<GovernedContentAcquirer>? logger = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _limiters = limiters ?? throw new ArgumentNullException(nameof(limiters));
        _robots = robots ?? throw new ArgumentNullException(nameof(robots));
        _options = options ?? new AcquisitionOptions();
        _mode = mode;
        _cache = cache;
        _cachePolicy = new CachePolicy(_options.EffectiveCache);
        _clock = clock ?? TimeProvider.System;
        _breaker = breaker ?? new BlockCircuitBreaker(_options, _clock, metrics);
        _challenges = challenges ?? new ChallengeDetector();
        _resilience = new AcquisitionResiliencePipeline(_options.EffectiveRetry, _clock);
        _adaptive = adaptive ?? new AdaptiveRateController(_limiters, metrics);
        _compliance = compliance;
        _metrics = metrics;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<AcquiredContent> AcquireAsync(AcquisitionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Offline replay owes no host anything: no robots fetch, no token, no politeness gap, no breaker.
        if (_options.Offline)
        {
            return await _transport.AcquireAsync(request, ct).ConfigureAwait(false);
        }

        var host = request.Url.Host;
        using var span = Activity.StartActivity("sanare.acquisition.fetch", ActivityKind.Client);
        span?.SetTag("host", host);
        span?.SetTag("sanare.source_id", request.SourceId);

        _breaker.ThrowIfOpen(host);
        await EnforceRobotsAsync(request, ct).ConfigureAwait(false);

        if (await TryServeFromCacheAsync(request, span, ct).ConfigureAwait(false) is { } cached)
        {
            return cached;
        }

        await using var lease = await _limiters.AcquireAsync(host, ct).ConfigureAwait(false);
        await WaitForPolitenessAsync(host, ct).ConfigureAwait(false);

        var content = await SendWithRetriesAsync(request, host, ct).ConfigureAwait(false);
        await RecordOutcomeAsync(request, content, span, ct).ConfigureAwait(false);
        return content;
    }

    private async ValueTask EnforceRobotsAsync(AcquisitionRequest request, CancellationToken ct)
    {
        if (!_options.EffectiveRobots.Enabled)
        {
            return;
        }

        var decision = await _robots.EvaluateAsync(request.Url, _mode, ct).ConfigureAwait(false);
        _compliance?.RecordRobotsDecision(
            request.SourceId,
            $"{(decision.Allowed ? "allowed" : "disallowed")} for '{decision.MatchedUserAgent}': {decision.Reason}");
        _limiters.ApplyCrawlDelay(request.Url.Host, decision.CrawlDelay);

        if (!decision.Allowed)
        {
            _metrics?.RecordBlocked(request.Url.Host, "robots");
            throw new AcquisitionException("SNR-ACQ-004", $"robots.txt disallows '{request.Url}': {decision.Reason}");
        }
    }

    private async ValueTask<AcquiredContent?> TryServeFromCacheAsync(AcquisitionRequest request, Activity? span, CancellationToken ct)
    {
        if (_cache is null || !_cachePolicy.Enabled)
        {
            return null;
        }

        var entry = await _cache.GetAsync(request.Url, ct).ConfigureAwait(false);
        if (entry is null || !entry.IsFresh(_clock.GetUtcNow()))
        {
            return null;
        }

        span?.SetTag("sanare.cache", "hit");
        _metrics?.RecordRequest(request.Url.Host, "cache", request.Tier.ToString());

        return new AcquiredContent(
            request.Url,
            new Uri(entry.Url),
            entry.StatusCode,
            entry.ContentType,
            System.Text.Encoding.UTF8,
            entry.Body,
            entry.Headers,
            ContentOrigin.Cache,
            FixtureId: null,
            Elapsed: TimeSpan.Zero);
    }

    private async ValueTask WaitForPolitenessAsync(string host, CancellationToken ct)
    {
        var before = _clock.GetTimestamp();
        await _limiters.WaitForPolitenessAsync(host, ct).ConfigureAwait(false);
        var waited = _clock.GetElapsedTime(before);
        if (waited > TimeSpan.Zero)
        {
            _metrics?.RecordDelay(waited.TotalMilliseconds, host, "politeness");
        }
    }

    private async ValueTask<AcquiredContent> SendWithRetriesAsync(AcquisitionRequest request, string host, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            AcquiredContent content;
            try
            {
                content = await _transport.AcquireAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                var transportDecision = _resilience.ClassifyTransportFailure(attempt);
                if (transportDecision.Disposition != RetryDisposition.Retry)
                {
                    throw new AcquisitionException("SNR-ACQ-001", $"The transport to '{host}' failed: {exception.Message}");
                }

                _logger?.LogDebug("Retrying {Host} after a transport failure: {Reason}", host, transportDecision.Reason);
                await DelayAsync(transportDecision, host, ct).ConfigureAwait(false);
                continue;
            }

            var severity = _challenges.Classify(content);
            var decision = _resilience.Classify(
                content.StatusCode,
                attempt,
                content.Headers.TryGetValue("Retry-After", out var retryAfter) ? retryAfter : null,
                severity);

            if (decision.Disposition != RetryDisposition.Retry)
            {
                return content;
            }

            _logger?.LogDebug("Retrying {Host}: {Reason}", host, decision.Reason);
            await DelayAsync(decision, host, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask DelayAsync(RetryDecision decision, string host, CancellationToken ct)
    {
        if (decision.Delay > TimeSpan.Zero)
        {
            _metrics?.RecordDelay(decision.Delay.TotalMilliseconds, host, "retry");
        }

        await _resilience.WaitAsync(decision, ct).ConfigureAwait(false);
    }

    private async ValueTask RecordOutcomeAsync(AcquisitionRequest request, AcquiredContent content, Activity? span, CancellationToken ct)
    {
        var host = request.Url.Host;
        var severity = _challenges.Classify(content);
        var blocked = severity != ChallengeSeverity.None || content.StatusCode is 403 or 429;

        _adaptive.Observe(host, content.StatusCode, severity != ChallengeSeverity.None);
        _metrics?.RecordRequest(host, $"{content.StatusCode / 100}xx", request.Tier.ToString());
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
        await WriteToCacheAsync(request, content, ct).ConfigureAwait(false);
    }

    private async ValueTask WriteToCacheAsync(AcquisitionRequest request, AcquiredContent content, CancellationToken ct)
    {
        if (_cache is null || !_cachePolicy.Enabled)
        {
            return;
        }

        var cacheControl = ParseCacheControl(content.Headers);
        if (!_cachePolicy.CanStore(content.StatusCode, cacheControl))
        {
            return;
        }

        var headers = CachePolicy.Sanitize(content.Headers);
        var now = _clock.GetUtcNow();
        var entry = new CachedResponse(
            FileHttpResponseCache.ComputeKey(request.Url),
            content.FinalUrl.AbsoluteUri,
            content.StatusCode,
            content.ContentType,
            content.Body,
            headers,
            now,
            _cachePolicy.ComputeExpiry(now, cacheControl, ReadExpires(headers)),
            CachePolicy.ReadETag(headers),
            CachePolicy.ReadLastModified(headers));

        await _cache.SetAsync(entry, ct).ConfigureAwait(false);
    }

    private static System.Net.Http.Headers.CacheControlHeaderValue? ParseCacheControl(IReadOnlyDictionary<string, string> headers) =>
        headers.TryGetValue("Cache-Control", out var raw)
            && System.Net.Http.Headers.CacheControlHeaderValue.TryParse(raw, out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset? ReadExpires(IReadOnlyDictionary<string, string> headers) =>
        headers.TryGetValue("Expires", out var raw)
            && DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
}
