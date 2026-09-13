using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity;
using Sanare.Http.Politeness;

namespace Sanare.Http.Robots;

/// <summary>
/// Fetches, caches, and evaluates <c>robots.txt</c> for every host acquisition touches
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "robots.txt").
/// </summary>
/// <remarks>
/// <para>
/// The document is fetched once per host and held in memory for 24 hours, mirrored on disk under the
/// configured cache root. It is fetched in <em>every</em> mode, not only <see cref="AcquisitionMode.Compliance"/>,
/// because <c>Crawl-delay</c> and <c>llms.txt</c> discovery both depend on it.
/// </para>
/// <para>
/// The fetch deliberately uses a minimal path — a bare <see cref="HttpClient"/> send with the identity's
/// headers — rather than the governed pipeline. Routing it through the pipeline would mean the robots
/// check needs a robots check, which cannot terminate. The host limiter is still taken so the robots
/// fetch itself stays polite.
/// </para>
/// <para>
/// Failure is never fatal: an unreachable document, a timeout, a <c>404</c>, or a <c>5xx</c> after the
/// short retry budget all resolve to unrestricted access (AC-ACQ-015). Sanare must not deny an operator
/// a host because that host's robots endpoint is down.
/// </para>
/// </remarks>
public sealed class RobotsPolicy : IRobotsPolicy
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _client;
    private readonly AcquisitionOptions _options;
    private readonly IHostLimiterRegistry? _limiters;
    private readonly TimeProvider _clock;
    private readonly string _userAgentToken;
    private readonly string? _diskRoot;

    /// <summary>Creates a policy fetching through <paramref name="client"/>.</summary>
    /// <param name="client">The transport used for the minimal robots fetch.</param>
    /// <param name="options">Supplies the robots and cache policy.</param>
    /// <param name="limiters">Paces robots fetches against the same host budget as target traffic.</param>
    /// <param name="clock">Drives cache expiry. Inject a fake in tests.</param>
    /// <param name="userAgentToken">The product token matched against <c>User-agent</c> groups.</param>
    /// <param name="diskRoot">The directory mirroring fetched documents, or <see langword="null"/> to stay in memory.</param>
    public RobotsPolicy(
        HttpClient client,
        AcquisitionOptions? options = null,
        IHostLimiterRegistry? limiters = null,
        TimeProvider? clock = null,
        string userAgentToken = "Sanare",
        string? diskRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgentToken);
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? new AcquisitionOptions();
        _limiters = limiters;
        _clock = clock ?? TimeProvider.System;
        _userAgentToken = userAgentToken;
        _diskRoot = diskRoot is null ? null : Path.Combine(diskRoot, "robots");
    }

    /// <inheritdoc />
    public async ValueTask<RobotsDecision> EvaluateAsync(Uri url, AcquisitionMode mode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri) { throw new ArgumentException("The robots evaluation URL must be absolute.", nameof(url)); }

        if (!_options.EffectiveRobots.Enabled)
        {
            return new RobotsDecision(Allowed: true, "*", CrawlDelay: null, "robots.txt evaluation is disabled by configuration.");
        }

        var ruleSet = await GetRuleSetAsync(url.Host, ct).ConfigureAwait(false);
        var allowed = ruleSet.IsAllowed(url.PathAndQuery);
        _limiters?.ApplyCrawlDelay(url.Host, ruleSet.CrawlDelay);

        if (allowed)
        {
            return new RobotsDecision(true, ruleSet.MatchedUserAgent, ruleSet.CrawlDelay, $"Allowed by the '{ruleSet.MatchedUserAgent}' group.");
        }

        // Stealth does not bypass robots silently; it records the decision and proceeds down the normal
        // governed path, which is why the decision object carries Allowed = true here. The caller records
        // the reason through ComplianceReporter so the choice stays auditable.
        return mode == AcquisitionMode.Stealth
            ? new RobotsDecision(true, ruleSet.MatchedUserAgent, ruleSet.CrawlDelay, $"Disallowed by the '{ruleSet.MatchedUserAgent}' group; proceeding under AcquisitionMode.Stealth.")
            : new RobotsDecision(false, ruleSet.MatchedUserAgent, ruleSet.CrawlDelay, $"Disallowed by the '{ruleSet.MatchedUserAgent}' group.");
    }

    /// <inheritdoc />
    public async ValueTask<RobotsRuleSet> GetRuleSetAsync(string host, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var now = _clock.GetUtcNow();
        if (_cache.TryGetValue(host, out var cached) && cached.ExpiresUtc > now)
        {
            return cached.RuleSet;
        }

        var content = await FetchAsync(host, ct).ConfigureAwait(false);
        var ruleSet = content is null ? RobotsRuleSet.Unrestricted : RobotsTxtParser.Parse(content, _userAgentToken);
        _cache[host] = new CacheEntry(ruleSet, now + _options.EffectiveRobots.EffectiveCacheLifetime);
        return ruleSet;
    }

    private async ValueTask<string?> FetchAsync(string host, CancellationToken ct)
    {
        var attempts = _options.EffectiveRobots.FetchRetryBudget + 1;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            IAsyncDisposable? lease = null;
            try
            {
                if (_limiters is not null)
                {
                    lease = await _limiters.AcquireAsync(host, ct).ConfigureAwait(false);
                }

                var url = new Uri($"https://{host}/robots.txt");
                using var message = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    // An absent robots file is an explicit "no restrictions", not a failure to retry.
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var bytes = await Sanare.Core.Acquisition.Content.BoundedStreamReader
                    .ReadAsync(stream, _options.MaxDiscoveryDocumentBytes, "SNR-ACQ-010", ct).ConfigureAwait(false);
                var text = Encoding.UTF8.GetString(bytes);
                await MirrorAsync(host, text, ct).ConfigureAwait(false);
                return text;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or AcquisitionException)
            {
                // Fall through to the next attempt, then to fail-open.
            }
            finally
            {
                if (lease is not null) { await lease.DisposeAsync().ConfigureAwait(false); }
            }
        }

        return null;
    }

    private async ValueTask MirrorAsync(string host, string content, CancellationToken ct)
    {
        if (_diskRoot is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_diskRoot);
            var safeHost = string.Join('_', host.Split(Path.GetInvalidFileNameChars()));
            await File.WriteAllTextAsync(Path.Combine(_diskRoot, safeHost + ".txt"), content, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The disk mirror is an operator convenience, not a correctness requirement. A read-only or
            // full volume must not deny the host.
        }
    }

    private sealed record CacheEntry(RobotsRuleSet RuleSet, DateTimeOffset ExpiresUtc);
}
