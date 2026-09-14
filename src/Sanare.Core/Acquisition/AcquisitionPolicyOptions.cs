namespace Sanare.Core.Acquisition;

/// <summary>
/// How the per-host request budget is chosen (docs/features/acquisition-pipeline.md,
/// "Key Behaviors" &gt; "Rate limiting and politeness").
/// </summary>
public enum RateLimitMode
{
    /// <summary>The static token bucket described by <see cref="RateLimitOptions.RequestsPerMinute"/>. The default.</summary>
    Fixed,

    /// <summary>
    /// An AIMD controller wraps the same token bucket, growing the effective rate on clean windows and
    /// halving it on any <c>429</c>/<c>503</c>/<c>403</c>/challenge signal.
    /// </summary>
    Adaptive,
}

/// <summary>
/// Per-host pacing policy. Validated at construction so a bad deployment configuration fails at
/// composition rather than at the first request (AC-ACQ-032).
/// </summary>
/// <param name="Mode">Whether the effective rate is static or AIMD-controlled.</param>
/// <param name="RequestsPerMinute">The steady-state token replenishment rate per host.</param>
/// <param name="Burst">The token-bucket depth, i.e. how many requests may be issued back to back.</param>
/// <param name="MaxRequestsPerMinute">The ceiling the adaptive controller may never exceed. Defaults to <paramref name="RequestsPerMinute"/> when omitted.</param>
/// <param name="MaxConcurrencyPerHost">How many requests may be in flight against one host at once.</param>
/// <param name="MinHostDelay">The configured politeness floor between two requests to the same host.</param>
/// <param name="JitterFraction">The symmetric fraction of <paramref name="MinHostDelay"/> applied as jitter, in <c>[0, 1)</c>.</param>
public sealed record RateLimitOptions(
    RateLimitMode Mode = RateLimitMode.Fixed,
    int RequestsPerMinute = 20,
    int Burst = 5,
    int? MaxRequestsPerMinute = null,
    int MaxConcurrencyPerHost = 2,
    TimeSpan? MinHostDelay = null,
    double JitterFraction = 0.20)
{
    /// <summary>The politeness floor actually applied when no <c>robots.txt</c> <c>Crawl-delay</c> is larger.</summary>
    public TimeSpan EffectiveMinHostDelay { get; } = Validate(RequestsPerMinute, Burst, MaxRequestsPerMinute, MaxConcurrencyPerHost, MinHostDelay, JitterFraction);

    /// <summary>The adaptive ceiling, resolved to <see cref="RequestsPerMinute"/> when unset.</summary>
    public int EffectiveMaxRequestsPerMinute => MaxRequestsPerMinute ?? RequestsPerMinute;

    private static TimeSpan Validate(int requestsPerMinute, int burst, int? maxRequestsPerMinute, int maxConcurrencyPerHost, TimeSpan? minHostDelay, double jitterFraction)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requestsPerMinute, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(burst, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrencyPerHost, 1);
        if (maxRequestsPerMinute is { } ceiling)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(ceiling, requestsPerMinute);
        }

        if (jitterFraction is < 0 or >= 1 || double.IsNaN(jitterFraction))
        {
            throw new ArgumentOutOfRangeException(nameof(jitterFraction), jitterFraction, "Jitter fraction must be in [0, 1).");
        }

        var delay = minHostDelay ?? TimeSpan.FromMilliseconds(1500);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        return delay;
    }
}

/// <summary>
/// <c>robots.txt</c> acquisition and caching policy (docs/features/acquisition-pipeline.md,
/// "Key Behaviors" &gt; "robots.txt").
/// </summary>
/// <param name="Enabled">Whether robots rules are fetched and evaluated at all. Disabling this is a deliberate, audited choice.</param>
/// <param name="CacheLifetime">How long a fetched ruleset stays authoritative before it is re-fetched.</param>
/// <param name="FetchRetryBudget">How many additional attempts a failing <c>robots.txt</c> fetch gets before the policy fails open.</param>
public sealed record RobotsOptions(
    bool Enabled = true,
    TimeSpan? CacheLifetime = null,
    int FetchRetryBudget = 1)
{
    /// <summary>The resolved cache lifetime; 24 hours by default.</summary>
    public TimeSpan EffectiveCacheLifetime { get; } = Validate(CacheLifetime, FetchRetryBudget);

    private static TimeSpan Validate(TimeSpan? cacheLifetime, int fetchRetryBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fetchRetryBudget);
        var lifetime = cacheLifetime ?? TimeSpan.FromHours(24);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        return lifetime;
    }
}

/// <summary>
/// Retry policy for transient acquisition failures (docs/features/acquisition-pipeline.md,
/// "Key Behaviors" &gt; "Retry and circuit breaking"). These numbers deliberately supersede
/// tech-design §7.4's larger values, which cannot coexist with the 30-minute run wall-clock.
/// </summary>
/// <param name="MaxAttempts">Total attempts including the first, so <c>3</c> means at most two retries.</param>
/// <param name="BaseDelay">The exponential backoff base, subject to full jitter.</param>
/// <param name="MaxDelay">The ceiling for any single computed backoff delay.</param>
/// <param name="RetryAfterCap">The largest server-supplied <c>Retry-After</c> the pipeline will honour by waiting. Beyond it the request fails immediately with <c>SNR-ACQ-002</c>.</param>
public sealed record RetryOptions(
    int MaxAttempts = 3,
    TimeSpan? BaseDelay = null,
    TimeSpan? MaxDelay = null,
    TimeSpan? RetryAfterCap = null)
{
    /// <summary>The resolved exponential backoff base; 500 ms by default.</summary>
    public TimeSpan EffectiveBaseDelay { get; } = ValidateBase(MaxAttempts, BaseDelay);

    /// <summary>The resolved single-delay ceiling; 30 seconds by default.</summary>
    public TimeSpan EffectiveMaxDelay { get; } = ValidateMax(BaseDelay, MaxDelay);

    /// <summary>The resolved <c>Retry-After</c> honour cap; 120 seconds by default.</summary>
    public TimeSpan EffectiveRetryAfterCap { get; } = ValidateCap(RetryAfterCap);

    private static TimeSpan ValidateBase(int maxAttempts, TimeSpan? baseDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        var value = baseDelay ?? TimeSpan.FromMilliseconds(500);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
        return value;
    }

    private static TimeSpan ValidateMax(TimeSpan? baseDelay, TimeSpan? maxDelay)
    {
        var resolvedBase = baseDelay ?? TimeSpan.FromMilliseconds(500);
        var value = maxDelay ?? TimeSpan.FromSeconds(30);
        ArgumentOutOfRangeException.ThrowIfLessThan(value, resolvedBase);
        return value;
    }

    private static TimeSpan ValidateCap(TimeSpan? retryAfterCap)
    {
        var value = retryAfterCap ?? TimeSpan.FromSeconds(120);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
        return value;
    }
}

/// <summary>
/// Circuit-breaker policy (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt;
/// "Retry and circuit breaking"). The two thresholds are deliberately separate: a generic block
/// streak opens a self-closing breaker, while a hard challenge opens an indefinite one.
/// </summary>
/// <param name="BlockThreshold">How many block responses within <paramref name="BlockWindow"/> open the generic breaker.</param>
/// <param name="BlockWindow">The sliding window the block streak is counted over.</param>
/// <param name="OpenDuration">How long the generic breaker stays open, and the floor for the challenge re-probe interval.</param>
/// <param name="ChallengeThreshold">How many challenge signatures open the indefinite challenge breaker.</param>
public sealed record BreakerOptions(
    int BlockThreshold = 5,
    TimeSpan? BlockWindow = null,
    TimeSpan? OpenDuration = null,
    int ChallengeThreshold = 5)
{
    /// <summary>The resolved block-streak window; 5 minutes by default.</summary>
    public TimeSpan EffectiveBlockWindow { get; } = ValidateWindow(BlockThreshold, ChallengeThreshold, BlockWindow);

    /// <summary>The resolved open duration; 30 minutes by default.</summary>
    public TimeSpan EffectiveOpenDuration { get; } = ValidateOpenDuration(OpenDuration);

    private static TimeSpan ValidateWindow(int blockThreshold, int challengeThreshold, TimeSpan? blockWindow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(blockThreshold, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(challengeThreshold, 1);
        var value = blockWindow ?? TimeSpan.FromMinutes(5);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
        return value;
    }

    private static TimeSpan ValidateOpenDuration(TimeSpan? openDuration)
    {
        var value = openDuration ?? TimeSpan.FromMinutes(30);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
        return value;
    }
}

/// <summary>
/// Conditional HTTP cache policy (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Caching").
/// </summary>
/// <param name="Enabled">Whether the response cache participates in acquisition at all.</param>
/// <param name="MinFreshness">The floor a server-declared freshness lifetime is clamped up to.</param>
/// <param name="MaxFreshness">The ceiling a server-declared freshness lifetime is clamped down to.</param>
/// <param name="Root">The directory the cache writes into. Relative paths resolve against the fixture-corpus root.</param>
public sealed record CacheOptions(
    bool Enabled = true,
    TimeSpan? MinFreshness = null,
    TimeSpan? MaxFreshness = null,
    string Root = "cache/http")
{
    /// <summary>The resolved freshness floor; 5 minutes by default.</summary>
    public TimeSpan EffectiveMinFreshness { get; } = ValidateMin(MinFreshness, Root);

    /// <summary>The resolved freshness ceiling; 7 days by default.</summary>
    public TimeSpan EffectiveMaxFreshness { get; } = ValidateMax(MinFreshness, MaxFreshness);

    private static TimeSpan ValidateMin(TimeSpan? minFreshness, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var value = minFreshness ?? TimeSpan.FromMinutes(5);
        ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
        return value;
    }

    private static TimeSpan ValidateMax(TimeSpan? minFreshness, TimeSpan? maxFreshness)
    {
        var resolvedMin = minFreshness ?? TimeSpan.FromMinutes(5);
        var value = maxFreshness ?? TimeSpan.FromDays(7);
        ArgumentOutOfRangeException.ThrowIfLessThan(value, resolvedMin);
        return value;
    }
}

/// <summary>
/// Browser tier (Tier 3, Playwright) governance policy (docs/features/browser-tier.md, "Task order" T1).
/// Disabled by default: DR-004's double opt-in requires both this global flag and the per-source
/// <see cref="AcquisitionPolicyOverride.AllowBrowserTier"/> before a browser is ever launched.
/// </summary>
/// <param name="Enabled">Whether the browser tier may run at all. The first half of the DR-004 double opt-in.</param>
/// <param name="MaxContexts">The bounded concurrency cap on simultaneously open browser contexts.</param>
/// <param name="BrowserWaitTimeout">How long <c>RentAsync</c> waits for a context to become available before failing <c>SNR-BRW-002</c>.</param>
/// <param name="MaxOperationsPerContext">How many operations a context serves before it is recycled.</param>
/// <param name="MaxContextAge">How long a context lives before it is recycled regardless of operation count.</param>
/// <param name="ContextIdleTimeout">How long an unused context sits in the pool before eviction.</param>
/// <param name="MaxScrolls">The clamp applied to a plan-requested scroll count (AC-BRW-008).</param>
/// <param name="ScrollDelay">The pause between successive scroll operations.</param>
/// <param name="BlockResources">Whether image/media/font/analytics requests are aborted by default (resource blocking).</param>
/// <param name="CaptureNetwork">Whether a HAR-style network log is captured for endpoint discovery. Off by default; not used during normal runs.</param>
/// <param name="BlockedHosts">The analytics/ads host block-list applied when <paramref name="BlockResources"/> is enabled.</param>
public sealed record BrowserOptions(
    bool Enabled = false,
    int MaxContexts = 2,
    TimeSpan? BrowserWaitTimeout = null,
    int MaxOperationsPerContext = 50,
    TimeSpan? MaxContextAge = null,
    TimeSpan? ContextIdleTimeout = null,
    int MaxScrolls = 50,
    TimeSpan? ScrollDelay = null,
    bool BlockResources = true,
    bool CaptureNetwork = false,
    IReadOnlyList<string>? BlockedHosts = null)
{
    private static readonly IReadOnlyList<string> DefaultBlockedHosts =
    [
        "google-analytics.com",
        "googletagmanager.com",
        "doubleclick.net",
        "facebook.net",
        "connect.facebook.net",
        "hotjar.com",
        "segment.io",
    ];

    /// <summary>The resolved pool-wait timeout; 30 seconds by default.</summary>
    public TimeSpan EffectiveBrowserWaitTimeout { get; } = ValidateWaitTimeout(BrowserWaitTimeout);

    /// <summary>The resolved context max age; 15 minutes by default.</summary>
    public TimeSpan EffectiveMaxContextAge { get; } = ValidateMaxContextAge(MaxContextAge);

    /// <summary>The resolved idle-context eviction timeout; 2 minutes by default.</summary>
    public TimeSpan EffectiveContextIdleTimeout { get; } = ValidateContextIdleTimeout(ContextIdleTimeout);

    /// <summary>The resolved inter-scroll delay; 250 ms by default.</summary>
    public TimeSpan EffectiveScrollDelay { get; } = ValidateScrollDelay(MaxContexts, MaxOperationsPerContext, MaxScrolls, ScrollDelay);

    /// <summary>The resolved analytics/ads block-list; a built-in default set when unspecified.</summary>
    public IReadOnlyList<string> EffectiveBlockedHosts { get; } = BlockedHosts ?? DefaultBlockedHosts;

    private static TimeSpan ValidateWaitTimeout(TimeSpan? browserWaitTimeout)
    {
        var value = browserWaitTimeout ?? TimeSpan.FromSeconds(30);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
        return value;
    }

    private static TimeSpan ValidateMaxContextAge(TimeSpan? maxContextAge)
    {
        var value = maxContextAge ?? TimeSpan.FromMinutes(15);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
        return value;
    }

    private static TimeSpan ValidateContextIdleTimeout(TimeSpan? contextIdleTimeout)
    {
        var value = contextIdleTimeout ?? TimeSpan.FromMinutes(2);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
        return value;
    }

    private static TimeSpan ValidateScrollDelay(int maxContexts, int maxOperationsPerContext, int maxScrolls, TimeSpan? scrollDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxContexts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxOperationsPerContext, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxScrolls, 1);
        var value = scrollDelay ?? TimeSpan.FromMilliseconds(250);
        ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
        return value;
    }
}

/// <summary>
/// A per-host or per-source override of the pacing and governance policy. Every member is optional;
/// an unset member inherits the corresponding root <see cref="AcquisitionOptions"/> value.
/// </summary>
/// <param name="RateLimit">Overrides the root pacing policy.</param>
/// <param name="Robots">Overrides the root robots policy.</param>
/// <param name="Retry">Overrides the root retry policy.</param>
/// <param name="Breaker">Overrides the root breaker policy.</param>
/// <param name="AllowBrowserTier">
/// The per-source half of the DR-004 double opt-in for the browser tier. <see langword="false"/> by
/// default: the global <see cref="AcquisitionOptions.Browser"/> flag alone is never sufficient (AC-007b).
/// </param>
/// <param name="RequiresImages">
/// Marks a source whose plan depends on lazy-loading (e.g. an image-triggered infinite scroll), which
/// disables resource blocking for that source when the wait strategy is <c>NetworkIdle</c> (AC-BRW-014).
/// </param>
public sealed record AcquisitionPolicyOverride(
    RateLimitOptions? RateLimit = null,
    RobotsOptions? Robots = null,
    RetryOptions? Retry = null,
    BreakerOptions? Breaker = null,
    bool AllowBrowserTier = false,
    bool RequiresImages = false);
