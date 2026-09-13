using System.Net.Http.Headers;
using Sanare.Core.Acquisition;
using Sanare.Http.Politeness;
using Sanare.Http.Resilience;

namespace Sanare.Http.Tests.Acquisition;

/// <summary>
/// Covers the pacing primitives named in the acquisition-pipeline Test Module's "Unit" scope:
/// <see cref="HostLimiterRegistry"/> partition identity, <see cref="HostBudget"/>'s politeness floor,
/// <see cref="PolitenessDelay"/> jitter bounds, <see cref="AdaptiveRateController"/>'s AIMD
/// trajectory and ceiling clamp, and <see cref="RetryAfterPolicy"/>'s two wire forms plus its cap.
/// </summary>
public sealed class PolitenessTests
{
    private static HostLimiterRegistry CreateRegistry(RateLimitOptions? rateLimit = null) =>
        new(new AcquisitionOptions(RateLimit: rateLimit ?? new RateLimitOptions()));

    [Fact]
    public async Task The_same_host_resolves_to_the_same_budget_regardless_of_casing()
    {
        // Two runs against one host must share a budget rather than doubling the configured load (AC-027).
        await using var registry = CreateRegistry();

        var first = registry.GetBudget("example.com");
        var second = registry.GetBudget("EXAMPLE.COM");

        Assert.Same(first, second);
    }

    [Fact]
    public async Task Different_hosts_resolve_to_different_budgets()
    {
        await using var registry = CreateRegistry();

        Assert.NotSame(registry.GetBudget("example.com"), registry.GetBudget("example.org"));
    }

    [Fact]
    public async Task A_longer_crawl_delay_raises_the_politeness_floor()
    {
        await using var registry = CreateRegistry(new RateLimitOptions(MinHostDelay: TimeSpan.FromMilliseconds(500)));

        registry.ApplyCrawlDelay("example.com", TimeSpan.FromSeconds(4));

        Assert.Equal(TimeSpan.FromSeconds(4), registry.GetBudget("example.com").MinimumDelay);
    }

    [Fact]
    public async Task A_shorter_crawl_delay_cannot_lower_the_configured_floor()
    {
        // A host asking for more patience is honoured; one asking for less cannot speed us up.
        await using var registry = CreateRegistry(new RateLimitOptions(MinHostDelay: TimeSpan.FromSeconds(3)));

        registry.ApplyCrawlDelay("example.com", TimeSpan.FromMilliseconds(10));

        Assert.Equal(TimeSpan.FromSeconds(3), registry.GetBudget("example.com").MinimumDelay);
    }

    [Fact]
    public async Task Clearing_a_crawl_delay_restores_the_configured_floor()
    {
        await using var registry = CreateRegistry(new RateLimitOptions(MinHostDelay: TimeSpan.FromSeconds(1)));
        registry.ApplyCrawlDelay("example.com", TimeSpan.FromSeconds(9));

        registry.ApplyCrawlDelay("example.com", null);

        Assert.Equal(TimeSpan.FromSeconds(1), registry.GetBudget("example.com").MinimumDelay);
    }

    [Fact]
    public void Jitter_keeps_every_draw_inside_the_configured_fraction()
    {
        var budget = new HostBudget("example.com", new RateLimitOptions(MinHostDelay: TimeSpan.FromSeconds(2), JitterFraction: 0.20));
        var delay = new PolitenessDelay(new Random(Seed: 1337));

        var lower = TimeSpan.FromSeconds(2 * 0.80);
        var upper = TimeSpan.FromSeconds(2 * 1.20);
        for (var i = 0; i < 500; i++)
        {
            var computed = delay.Compute(budget);
            Assert.InRange(computed, lower, upper);
        }
    }

    [Fact]
    public void A_zero_jitter_fraction_returns_the_floor_exactly()
    {
        var budget = new HostBudget("example.com", new RateLimitOptions(MinHostDelay: TimeSpan.FromSeconds(2), JitterFraction: 0));

        Assert.Equal(TimeSpan.FromSeconds(2), new PolitenessDelay(new Random(Seed: 1)).Compute(budget));
    }

    [Fact]
    public async Task Fixed_mode_pins_the_rate_to_the_configured_value()
    {
        // The default deployment must behave exactly as it did before the controller existed.
        await using var registry = CreateRegistry(new RateLimitOptions(RateLimitMode.Fixed, RequestsPerMinute: 20, MaxRequestsPerMinute: 60));
        var controller = new AdaptiveRateController(registry);

        for (var i = 0; i < 25; i++)
        {
            controller.Observe("example.com", statusCode: 200);
        }

        Assert.Equal(20, controller.GetEffectiveRate("example.com"));
    }

    [Fact]
    public async Task Adaptive_mode_increases_additively_on_success_up_to_the_ceiling()
    {
        await using var registry = CreateRegistry(new RateLimitOptions(RateLimitMode.Adaptive, RequestsPerMinute: 10, MaxRequestsPerMinute: 15));
        var controller = new AdaptiveRateController(registry);

        var start = controller.GetEffectiveRate("example.com");
        controller.Observe("example.com", statusCode: 200);
        var afterOne = controller.GetEffectiveRate("example.com");

        Assert.True(afterOne > start, $"expected additive increase from {start}, got {afterOne}");

        for (var i = 0; i < 100; i++)
        {
            controller.Observe("example.com", statusCode: 200);
        }

        Assert.Equal(15, controller.GetEffectiveRate("example.com"));
    }

    [Fact]
    public async Task Adaptive_mode_decreases_multiplicatively_on_pushback()
    {
        await using var registry = CreateRegistry(new RateLimitOptions(RateLimitMode.Adaptive, RequestsPerMinute: 20, MaxRequestsPerMinute: 40));
        var controller = new AdaptiveRateController(registry);

        var before = controller.GetEffectiveRate("example.com");
        controller.Observe("example.com", statusCode: 429);
        var after = controller.GetEffectiveRate("example.com");

        Assert.True(after < before, $"expected multiplicative decrease from {before}, got {after}");
    }

    [Fact]
    public async Task A_detected_challenge_pushes_the_rate_down_even_on_a_success_status()
    {
        await using var registry = CreateRegistry(new RateLimitOptions(RateLimitMode.Adaptive, RequestsPerMinute: 20, MaxRequestsPerMinute: 40));
        var controller = new AdaptiveRateController(registry);

        var before = controller.GetEffectiveRate("example.com");
        controller.Observe("example.com", statusCode: 200, challengeDetected: true);

        Assert.True(controller.GetEffectiveRate("example.com") < before);
    }

    [Fact]
    public async Task Pushback_never_raises_the_rate_no_matter_how_often_it_arrives()
    {
        // Reaction to a block signal is one-directional; that is what keeps the posture polite.
        await using var registry = CreateRegistry(new RateLimitOptions(RateLimitMode.Adaptive, RequestsPerMinute: 60, MaxRequestsPerMinute: 120));
        var controller = new AdaptiveRateController(registry);

        var previous = controller.GetEffectiveRate("example.com");
        for (var i = 0; i < 20; i++)
        {
            controller.Observe("example.com", statusCode: 503);
            var current = controller.GetEffectiveRate("example.com");
            Assert.True(current <= previous, $"rate rose from {previous} to {current} on pushback");
            previous = current;
        }
    }

    [Fact]
    public void An_absent_retry_after_header_reports_no_value()
    {
        var result = RetryAfterPolicy.Read(CreateHeaders(retryAfter: null), DateTimeOffset.UtcNow, TimeSpan.FromSeconds(120));

        Assert.False(result.HasValue);
        Assert.False(result.ExceedsCap);
    }

    [Fact]
    public void A_delta_seconds_retry_after_is_honoured()
    {
        var headers = CreateHeaders(new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)));

        var result = RetryAfterPolicy.Read(headers, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(120));

        Assert.True(result.HasValue);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Delay);
        Assert.False(result.ExceedsCap);
    }

    [Fact]
    public void An_http_date_retry_after_is_converted_relative_to_now()
    {
        var now = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var headers = CreateHeaders(new RetryConditionHeaderValue(now.AddSeconds(45)));

        var result = RetryAfterPolicy.Read(headers, now, TimeSpan.FromSeconds(120));

        Assert.True(result.HasValue);
        Assert.Equal(TimeSpan.FromSeconds(45), result.Delay);
        Assert.False(result.ExceedsCap);
    }

    [Fact]
    public void An_http_date_already_in_the_past_clamps_to_zero_rather_than_going_negative()
    {
        var now = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var headers = CreateHeaders(new RetryConditionHeaderValue(now.AddSeconds(-60)));

        var result = RetryAfterPolicy.Read(headers, now, TimeSpan.FromSeconds(120));

        Assert.True(result.HasValue);
        Assert.True(result.Delay >= TimeSpan.Zero, $"expected a non-negative delay, got {result.Delay}");
    }

    [Fact]
    public void A_retry_after_beyond_the_cap_is_flagged_rather_than_slept_off()
    {
        // Holding a worker for ten minutes would blow the run's wall-clock budget while looking like a hang.
        var headers = CreateHeaders(new RetryConditionHeaderValue(TimeSpan.FromMinutes(10)));

        var result = RetryAfterPolicy.Read(headers, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(120));

        Assert.True(result.HasValue);
        Assert.True(result.ExceedsCap);
    }

    private static HttpResponseHeaders CreateHeaders(RetryConditionHeaderValue? retryAfter)
    {
        using var response = new HttpResponseMessage();
        response.Headers.RetryAfter = retryAfter;
        return response.Headers;
    }
}
