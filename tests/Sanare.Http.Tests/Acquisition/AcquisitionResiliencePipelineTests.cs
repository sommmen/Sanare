using Sanare.Core.Acquisition;
using Sanare.Http.Resilience;

namespace Sanare.Http.Tests.Acquisition;

public sealed class AcquisitionResiliencePipelineTests
{
    private static AcquisitionResiliencePipeline Create(RetryOptions? retry = null, FakeTimeProvider? clock = null) =>
        new(retry ?? new RetryOptions(), clock ?? new FakeTimeProvider(), new Random(Seed: 20240101));

    [Theory]
    [InlineData(408)]
    [InlineData(425)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void Transient_statuses_are_retried(int statusCode)
    {
        var decision = Create().Classify(statusCode, attempt: 0);
        Assert.Equal(RetryDisposition.Retry, decision.Disposition);
    }

    [Fact]
    public void Forbidden_is_never_retried()
    {
        var decision = Create().Classify(403, attempt: 0);
        Assert.Equal(RetryDisposition.Block, decision.Disposition);
        Assert.Equal(TimeSpan.Zero, decision.Delay);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public void Absent_resources_are_returned_intact(int statusCode)
    {
        // A 404 is data, not a failure: a plan's notFound predicate has to be able to see it.
        var decision = Create().Classify(statusCode, attempt: 0);
        Assert.Equal(RetryDisposition.NotFound, decision.Disposition);
    }

    [Fact]
    public void A_challenge_signature_blocks_even_on_a_retryable_status()
    {
        var decision = Create().Classify(503, attempt: 0, severity: ChallengeSeverity.Hard);
        Assert.Equal(RetryDisposition.Block, decision.Disposition);
    }

    [Fact]
    public void The_retry_budget_is_capped()
    {
        var pipeline = Create(new RetryOptions(MaxAttempts: 3));

        Assert.Equal(RetryDisposition.Retry, pipeline.Classify(503, attempt: 2).Disposition);
        Assert.Equal(RetryDisposition.Accept, pipeline.Classify(503, attempt: 3).Disposition);
    }

    [Fact]
    public void Retry_after_is_honoured_within_the_cap()
    {
        var decision = Create().Classify(429, attempt: 0, retryAfterHeader: "5");

        Assert.Equal(RetryDisposition.Retry, decision.Disposition);
        Assert.Equal(TimeSpan.FromSeconds(5), decision.Delay);
    }

    [Fact]
    public void Retry_after_beyond_the_cap_fails_immediately_without_sleeping()
    {
        // AC-009b: the point of the cap is that we fail fast rather than block a worker for ten minutes.
        var clock = new FakeTimeProvider();
        var pipeline = Create(new RetryOptions(RetryAfterCap: TimeSpan.FromSeconds(120)), clock);

        var exception = Assert.Throws<AcquisitionException>(() => pipeline.Classify(429, attempt: 0, retryAfterHeader: "600"));

        Assert.Equal("SNR-ACQ-002", exception.Code);
        Assert.Equal(TimeSpan.Zero, clock.TotalAdvanced);
    }

    [Fact]
    public void Backoff_stays_within_the_configured_ceiling()
    {
        var retry = new RetryOptions(MaxAttempts: 10, BaseDelay: TimeSpan.FromMilliseconds(500), MaxDelay: TimeSpan.FromSeconds(30));

        var pipeline = Create(retry);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var decision = pipeline.Classify(503, attempt);
            Assert.InRange(decision.Delay, TimeSpan.Zero, retry.MaxDelay!.Value);
        }
    }

    [Fact]
    public void Backoff_grows_with_the_attempt_number()
    {
        // Full jitter means any single draw can be small, so the growth assertion is over an average.
        var pipeline = Create(new RetryOptions(MaxAttempts: 10, BaseDelay: TimeSpan.FromMilliseconds(500)));

        var early = AverageDelay(pipeline, attempt: 0);
        var late = AverageDelay(pipeline, attempt: 5);

        Assert.True(late > early * 4, $"Expected attempt 5 ({late}) to back off far beyond attempt 0 ({early}).");
    }

    [Fact]
    public void Transport_failures_retry_then_give_up()
    {
        var pipeline = Create(new RetryOptions(MaxAttempts: 2));

        Assert.Equal(RetryDisposition.Retry, pipeline.ClassifyTransportFailure(attempt: 0).Disposition);
        Assert.Equal(RetryDisposition.Retry, pipeline.ClassifyTransportFailure(attempt: 1).Disposition);
        Assert.Equal(RetryDisposition.Accept, pipeline.ClassifyTransportFailure(attempt: 2).Disposition);
    }

    [Fact]
    public async Task Waiting_advances_only_the_injected_clock()
    {
        var clock = new FakeTimeProvider();
        var pipeline = Create(clock: clock);
        var decision = new RetryDecision(RetryDisposition.Retry, TimeSpan.FromSeconds(30), "test");

        var wait = pipeline.WaitAsync(decision).AsTask();
        Assert.False(wait.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(30));
        await wait;
    }

    private static double AverageDelay(AcquisitionResiliencePipeline pipeline, int attempt)
    {
        var total = 0.0;
        for (var i = 0; i < 50; i++)
        {
            total += pipeline.Classify(503, attempt).Delay.TotalMilliseconds;
        }

        return total / 50;
    }
}
