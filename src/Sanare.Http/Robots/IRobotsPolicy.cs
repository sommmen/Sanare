using Sanare.Http.Identity;

namespace Sanare.Http.Robots;

/// <summary>The outcome of evaluating one URL against a host's <c>robots.txt</c>.</summary>
/// <param name="Allowed">Whether the fetch may proceed.</param>
/// <param name="MatchedUserAgent">The user-agent group that decided, or <c>*</c> for the fallback.</param>
/// <param name="CrawlDelay">The host's advertised <c>Crawl-delay</c>, which is a politeness floor in every mode.</param>
/// <param name="Reason">A short operator-facing explanation, suitable for a compliance record.</param>
public sealed record RobotsDecision(bool Allowed, string MatchedUserAgent, TimeSpan? CrawlDelay, string Reason);

/// <summary>
/// Evaluates target URLs against the host's <c>robots.txt</c> before a socket is opened for them
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "robots.txt").
/// </summary>
public interface IRobotsPolicy
{
    /// <summary>
    /// Fetches (or reuses a cached) <c>robots.txt</c> for <paramref name="url"/>'s host and evaluates the
    /// URL against it. Never throws for an unreachable robots file: an unavailable document resolves to
    /// unrestricted access (AC-ACQ-015).
    /// </summary>
    /// <param name="url">The absolute target URL.</param>
    /// <param name="mode">Whether an applicable <c>Disallow</c> blocks or is merely recorded.</param>
    /// <param name="ct">Cancels the robots fetch.</param>
    ValueTask<RobotsDecision> EvaluateAsync(Uri url, AcquisitionMode mode, CancellationToken ct = default);

    /// <summary>
    /// Returns the parsed rule set for <paramref name="host"/>, fetching it if necessary. Exposed so
    /// discovery-document resolution can read <c>Sitemap</c> lines without re-fetching.
    /// </summary>
    /// <param name="host">The host whose robots document is wanted.</param>
    /// <param name="ct">Cancels the fetch.</param>
    ValueTask<RobotsRuleSet> GetRuleSetAsync(string host, CancellationToken ct = default);
}
