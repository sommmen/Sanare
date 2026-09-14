using System.Net;
using System.Text;
using Sanare.Abstractions;
using Sanare.Abstractions.Plans;

namespace Sanare.Core.Acquisition;

public enum ContentOrigin { Network, Cache, Fixture, Browser }

/// <summary>
/// The wire-level shape of a composed browsing identity: an order-significant header list, cookie
/// contributions, and the profile id that produced them (docs/features/browsing-identity.md, T10).
/// Owned by <c>Sanare.Core</c> rather than <c>Sanare.Http</c> so <see cref="AcquisitionRequest"/> can
/// carry it without a circular project reference — <c>Sanare.Http</c> already depends on
/// <c>Sanare.Core</c>, so its richer <c>Sanare.Http.Identity.BrowsingIdentity</c> cannot appear here.
/// <c>Sanare.Http</c> projects a <c>BrowsingIdentity</c> into this shape before calling
/// <see cref="IContentAcquirer.AcquireAsync"/>.
/// </summary>
/// <param name="ProfileId">The identity profile that produced this identity, recorded on <see cref="AcquiredContent"/>.</param>
/// <param name="Headers">
/// The ordered header list to write onto the outgoing request, in list order. Never a dictionary —
/// header order is part of the identity contract and a dictionary cannot preserve it.
/// </param>
/// <param name="Cookies">Cookie contributions to send with the request, if any.</param>
public sealed record RequestIdentity(
    string ProfileId,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    IReadOnlyList<Cookie> Cookies);

public sealed record AcquisitionRequest(
    Uri Url,
    string SourceId,
    IReadOnlySet<string>? ExpectedContentTypes = null,
    string? PageRole = null,
    AcquisitionTier Tier = AcquisitionTier.Html,
    string Method = "GET",
    RequestIdentity? Identity = null,
    AcquisitionSpec? Acquisition = null)
{
    public IReadOnlySet<string> EffectiveExpectedContentTypes => ExpectedContentTypes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/html", "application/xhtml+xml" };
}

public sealed record AcquiredContent(
    Uri RequestedUrl,
    Uri FinalUrl,
    int StatusCode,
    string ContentType,
    Encoding Charset,
    ReadOnlyMemory<byte> Body,
    IReadOnlyDictionary<string, string> Headers,
    ContentOrigin Origin,
    string? FixtureId,
    TimeSpan Elapsed,
    string? IdentityProfileId = null);

/// <summary>
/// The complete acquisition policy surface (docs/features/acquisition-pipeline.md, "Implementation Plan"
/// &gt; delivery decision 6). Pacing lives here rather than on the extraction plan because plans are
/// git-committed, credential-free artefacts while pacing is deployment configuration.
/// </summary>
/// <param name="Offline">When set, acquisition replays from the fixture corpus and never opens a socket.</param>
/// <param name="AllowInsecureTransport">When set, <c>http://</c> targets and redirect hops are permitted.</param>
/// <param name="MaximumResponseBytes">The streaming byte ceiling for a target response body.</param>
/// <param name="RateLimit">Per-host pacing policy.</param>
/// <param name="Robots">robots.txt acquisition and caching policy.</param>
/// <param name="Retry">Transient-failure retry policy.</param>
/// <param name="Breaker">Circuit-breaker policy.</param>
/// <param name="Cache">Conditional HTTP cache policy.</param>
/// <param name="Browser">Browser tier (Tier 3) governance policy. Disabled by default (DR-004).</param>
/// <param name="MaxRedirects">The redirect hop ceiling before <c>SNR-ACQ-008</c>.</param>
/// <param name="MaxDiscoveryDocumentBytes">The streaming byte ceiling for an <c>llms.txt</c> discovery document.</param>
/// <param name="HostOverrides">Per-host policy overrides, keyed by lowercase host.</param>
/// <param name="SourceOverrides">Per-source policy overrides, keyed by source id. Applied after <paramref name="HostOverrides"/>.</param>
public sealed record AcquisitionOptions(
    bool Offline = false,
    bool AllowInsecureTransport = false,
    long MaximumResponseBytes = 16L * 1024 * 1024,
    RateLimitOptions? RateLimit = null,
    RobotsOptions? Robots = null,
    RetryOptions? Retry = null,
    BreakerOptions? Breaker = null,
    CacheOptions? Cache = null,
    BrowserOptions? Browser = null,
    int MaxRedirects = 10,
    long MaxDiscoveryDocumentBytes = 512L * 1024,
    IReadOnlyDictionary<string, AcquisitionPolicyOverride>? HostOverrides = null,
    IReadOnlyDictionary<string, AcquisitionPolicyOverride>? SourceOverrides = null)
{
    /// <summary>The resolved pacing policy.</summary>
    public RateLimitOptions EffectiveRateLimit { get; } = Validate(MaximumResponseBytes, MaxRedirects, MaxDiscoveryDocumentBytes, RateLimit);

    /// <summary>The resolved robots policy.</summary>
    public RobotsOptions EffectiveRobots { get; } = Robots ?? new RobotsOptions();

    /// <summary>The resolved retry policy.</summary>
    public RetryOptions EffectiveRetry { get; } = Retry ?? new RetryOptions();

    /// <summary>The resolved breaker policy.</summary>
    public BreakerOptions EffectiveBreaker { get; } = Breaker ?? new BreakerOptions();

    /// <summary>The resolved cache policy.</summary>
    public CacheOptions EffectiveCache { get; } = Cache ?? new CacheOptions();

    /// <summary>The resolved browser tier policy; disabled by default.</summary>
    public BrowserOptions EffectiveBrowser { get; } = Browser ?? new BrowserOptions();

    /// <summary>
    /// Resolves the policy applying to one request by layering the source override over the host
    /// override over the root policy, so the most specific configured value wins per member.
    /// </summary>
    /// <param name="host">The target host. Matched case-insensitively.</param>
    /// <param name="sourceId">
    /// The source id the request belongs to, or <see langword="null"/> for host-scoped callers such as the
    /// shared limiter registry, which paces a host across every source that targets it.
    /// </param>
    public ResolvedAcquisitionPolicy ResolveFor(string host, string? sourceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var hostOverride = Lookup(HostOverrides, host);
        var sourceOverride = sourceId is null ? null : Lookup(SourceOverrides, sourceId);

        return new ResolvedAcquisitionPolicy(
            sourceOverride?.RateLimit ?? hostOverride?.RateLimit ?? EffectiveRateLimit,
            sourceOverride?.Robots ?? hostOverride?.Robots ?? EffectiveRobots,
            sourceOverride?.Retry ?? hostOverride?.Retry ?? EffectiveRetry,
            sourceOverride?.Breaker ?? hostOverride?.Breaker ?? EffectiveBreaker,
            EffectiveBrowser,
            sourceOverride?.AllowBrowserTier ?? hostOverride?.AllowBrowserTier ?? false,
            sourceOverride?.RequiresImages ?? hostOverride?.RequiresImages ?? false);
    }

    private static AcquisitionPolicyOverride? Lookup(IReadOnlyDictionary<string, AcquisitionPolicyOverride>? overrides, string key)
    {
        if (overrides is null)
        {
            return null;
        }

        if (overrides.TryGetValue(key, out var exact))
        {
            return exact;
        }

        foreach (var candidate in overrides)
        {
            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Value;
            }
        }

        return null;
    }

    private static RateLimitOptions Validate(long maximumResponseBytes, int maxRedirects, long maxDiscoveryDocumentBytes, RateLimitOptions? rateLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumResponseBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRedirects, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDiscoveryDocumentBytes, 1);
        return rateLimit ?? new RateLimitOptions();
    }
}

/// <summary>The policy layer that applies to one specific request, produced by <see cref="AcquisitionOptions.ResolveFor"/>.</summary>
/// <param name="RateLimit">The pacing policy in force.</param>
/// <param name="Robots">The robots policy in force.</param>
/// <param name="Retry">The retry policy in force.</param>
/// <param name="Breaker">The breaker policy in force.</param>
/// <param name="Browser">The resolved browser tier governance policy (global, not per-source).</param>
/// <param name="AllowBrowserTier">
/// The per-source half of the DR-004 double opt-in; the browser tier is only permitted when this is
/// <see langword="true"/> AND <see cref="Browser"/>.Enabled is <see langword="true"/> (AC-007b).
/// </param>
/// <param name="RequiresImages">Whether this source's plan depends on lazily-loaded images, disabling resource blocking (AC-BRW-014).</param>
public sealed record ResolvedAcquisitionPolicy(
    RateLimitOptions RateLimit,
    RobotsOptions Robots,
    RetryOptions Retry,
    BreakerOptions Breaker,
    BrowserOptions Browser,
    bool AllowBrowserTier,
    bool RequiresImages);

public sealed class AcquisitionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IContentAcquirer
{
    ValueTask<AcquiredContent> AcquireAsync(AcquisitionRequest request, CancellationToken ct = default);
}
