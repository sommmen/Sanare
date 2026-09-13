using System.Collections.Concurrent;
using Sanare.Core.Acquisition;
using Sanare.Core.Observability;

namespace Sanare.Http.Politeness;

/// <summary>
/// An AIMD (additive-increase/multiplicative-decrease) controller over the per-host token budget
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Rate limiting and politeness", item 1).
/// </summary>
/// <remarks>
/// <para>
/// The controller is inert unless the host's resolved <see cref="RateLimitOptions.Mode"/> is
/// <see cref="RateLimitMode.Adaptive"/>: under <see cref="RateLimitMode.Fixed"/> every observation is
/// accepted and the effective rate stays pinned to the configured value, so the default deployment
/// behaves exactly as it did before this type existed.
/// </para>
/// <para>
/// Increase is additive and bounded by <see cref="RateLimitOptions.EffectiveMaxRequestsPerMinute"/>;
/// decrease is multiplicative and immediate. Pushback never raises the rate — reaction to a block signal
/// is one-directional, which is what keeps the "polite by default" posture intact even when the
/// controller is climbing.
/// </para>
/// </remarks>
public sealed class AdaptiveRateController
{
    private const double DecreaseFactor = 0.5;
    private const int AdditiveStepPerMinute = 1;

    private readonly ConcurrentDictionary<string, HostRate> _rates = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHostLimiterRegistry _registry;
    private readonly ScraperMetrics? _metrics;

    /// <summary>Creates a controller over <paramref name="registry"/>'s budgets.</summary>
    /// <param name="registry">Supplies the resolved per-host policy the controller clamps against.</param>
    /// <param name="metrics">Receives <c>sanare.acquisition.rate_limit</c>, when supplied.</param>
    public AdaptiveRateController(IHostLimiterRegistry registry, ScraperMetrics? metrics = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _metrics = metrics;
    }

    /// <summary>
    /// The requests-per-minute currently in force for <paramref name="host"/>. Under
    /// <see cref="RateLimitMode.Fixed"/> this is always the configured rate.
    /// </summary>
    /// <param name="host">The host to report on.</param>
    public double GetEffectiveRate(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var budget = _registry.GetBudget(host);
        return budget.RateLimit.Mode == RateLimitMode.Adaptive
            ? GetRate(host, budget).Current
            : budget.RateLimit.RequestsPerMinute;
    }

    /// <summary>
    /// Feeds one response back into the controller: a clean window grows the rate additively, while any
    /// pushback signal halves it immediately.
    /// </summary>
    /// <param name="host">The host the response came from.</param>
    /// <param name="statusCode">The HTTP status observed, or <c>0</c> for a transport failure.</param>
    /// <param name="challengeDetected">Whether a challenge signature was recognised in the response.</param>
    public void Observe(string host, int statusCode, bool challengeDetected = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var budget = _registry.GetBudget(host);
        if (budget.RateLimit.Mode != RateLimitMode.Adaptive)
        {
            return;
        }

        var rate = GetRate(host, budget);
        var pushback = challengeDetected || statusCode is 429 or 503 or 403;
        lock (rate.Gate)
        {
            rate.Current = pushback
                ? Math.Max(1.0, rate.Current * DecreaseFactor)
                : Math.Min(budget.RateLimit.EffectiveMaxRequestsPerMinute, rate.Current + AdditiveStepPerMinute);

            _metrics?.SetEffectiveRateLimit(rate.Current, host);
        }
    }

    private HostRate GetRate(string host, HostBudget budget) =>
        _rates.GetOrAdd(host, static (_, seed) => new HostRate(seed), (double)budget.RateLimit.RequestsPerMinute);

    private sealed class HostRate(double initial)
    {
        public Lock Gate { get; } = new();

        public double Current { get; set; } = initial;
    }
}
