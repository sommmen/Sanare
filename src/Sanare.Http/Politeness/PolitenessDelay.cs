namespace Sanare.Http.Politeness;

/// <summary>
/// Computes the jittered spacing applied between two requests to the same host
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Rate limiting and politeness", item 4).
/// The <see cref="Random"/> is injected so tests can seed it and assert an exact schedule.
/// </summary>
/// <param name="random">The jitter source. Seed it in tests.</param>
public sealed class PolitenessDelay(Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;

    /// <summary>
    /// Returns <c>max(configuredDelay, robotsCrawlDelay)</c> scaled by a uniform factor in
    /// <c>[1 - jitterFraction, 1 + jitterFraction]</c>. Never returns a negative delay.
    /// </summary>
    /// <param name="budget">The resolved host budget supplying the floor and the jitter fraction.</param>
    public TimeSpan Compute(HostBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);

        var baseDelay = budget.MinimumDelay;
        if (baseDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var jitterFraction = budget.RateLimit.JitterFraction;
        if (jitterFraction <= 0)
        {
            return baseDelay;
        }

        // NextDouble() is in [0, 1), so this maps to [1 - f, 1 + f).
        var scale = 1.0 + ((_random.NextDouble() * 2.0) - 1.0) * jitterFraction;
        var ticks = (long)(baseDelay.Ticks * scale);
        return ticks <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(ticks);
    }
}
