using Sanare.Core.Acquisition;

namespace Sanare.Http.Politeness;

/// <summary>
/// The resolved pacing settings in force for one host, combining the configured policy with any
/// <c>robots.txt</c> <c>Crawl-delay</c> the host advertises
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Rate limiting and politeness").
/// </summary>
/// <param name="Host">The lowercase host these settings apply to.</param>
/// <param name="RateLimit">The configured pacing policy.</param>
/// <param name="CrawlDelay">The host's advertised <c>Crawl-delay</c>, when it has one.</param>
public sealed record HostBudget(string Host, RateLimitOptions RateLimit, TimeSpan? CrawlDelay = null)
{
    /// <summary>
    /// The politeness floor actually applied: the larger of the configured delay and the host's
    /// advertised crawl delay. A host asking for more space always wins.
    /// </summary>
    public TimeSpan MinimumDelay =>
        CrawlDelay is { } crawlDelay && crawlDelay > RateLimit.EffectiveMinHostDelay
            ? crawlDelay
            : RateLimit.EffectiveMinHostDelay;

    /// <summary>Returns this budget with <paramref name="crawlDelay"/> applied.</summary>
    /// <param name="crawlDelay">The advertised crawl delay, or <see langword="null"/> to clear it.</param>
    public HostBudget WithCrawlDelay(TimeSpan? crawlDelay) => this with { CrawlDelay = crawlDelay };
}
