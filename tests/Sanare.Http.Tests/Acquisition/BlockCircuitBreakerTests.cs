using Sanare.Core.Acquisition;
using Sanare.Http.Resilience;

namespace Sanare.Http.Tests.Acquisition;

public sealed class BlockCircuitBreakerTests
{
    private const string Host = "example.test";

    private static (BlockCircuitBreaker Breaker, FakeTimeProvider Clock) Create(BreakerOptions? breaker = null)
    {
        var clock = new FakeTimeProvider();
        var options = new AcquisitionOptions(Breaker: breaker ?? new BreakerOptions());
        return (new BlockCircuitBreaker(options, clock), clock);
    }

    [Fact]
    public void Five_blocks_inside_the_window_open_the_circuit()
    {
        var (breaker, _) = Create(new BreakerOptions(BlockThreshold: 5));

        for (var i = 0; i < 5; i++)
        {
            breaker.RecordBlock(Host, ChallengeSeverity.Generic);
        }

        Assert.Equal(BreakerState.Blocked, breaker.GetState(Host));
        var exception = Assert.Throws<AcquisitionException>(() => breaker.ThrowIfOpen(Host));
        Assert.Equal("SNR-ACQ-003", exception.Code);
    }

    [Fact]
    public void Four_blocks_then_a_success_resets_the_streak()
    {
        // AC-010b: the boundary below the threshold must not leave a latent strike behind.
        var (breaker, _) = Create(new BreakerOptions(BlockThreshold: 5));

        for (var i = 0; i < 4; i++)
        {
            breaker.RecordBlock(Host, ChallengeSeverity.Generic);
        }

        breaker.RecordSuccess(Host);

        for (var i = 0; i < 4; i++)
        {
            breaker.RecordBlock(Host, ChallengeSeverity.Generic);
        }

        Assert.Equal(BreakerState.Closed, breaker.GetState(Host));
        breaker.ThrowIfOpen(Host);
    }

    [Fact]
    public void Blocks_outside_the_window_do_not_accumulate()
    {
        var (breaker, clock) = Create(new BreakerOptions(BlockThreshold: 5, BlockWindow: TimeSpan.FromMinutes(5)));

        for (var i = 0; i < 4; i++)
        {
            breaker.RecordBlock(Host, ChallengeSeverity.Generic);
        }

        clock.Advance(TimeSpan.FromMinutes(6));
        breaker.RecordBlock(Host, ChallengeSeverity.Generic);

        Assert.Equal(BreakerState.Closed, breaker.GetState(Host));
    }

    [Fact]
    public void An_open_circuit_closes_after_the_open_duration()
    {
        var (breaker, clock) = Create(new BreakerOptions(BlockThreshold: 2, OpenDuration: TimeSpan.FromMinutes(30)));

        breaker.RecordBlock(Host, ChallengeSeverity.Generic);
        breaker.RecordBlock(Host, ChallengeSeverity.Generic);
        Assert.Equal(BreakerState.Blocked, breaker.GetState(Host));

        clock.Advance(TimeSpan.FromMinutes(31));

        Assert.Equal(BreakerState.Closed, breaker.GetState(Host));
        breaker.ThrowIfOpen(Host);
    }

    [Fact]
    public void A_hard_challenge_pauses_rather_than_time_boxing()
    {
        // DR-014: a challenge is not a transient block, so it must not clear on a timer alone.
        var (breaker, clock) = Create(new BreakerOptions(ChallengeThreshold: 1));

        var state = breaker.RecordBlock(Host, ChallengeSeverity.Hard);

        Assert.Equal(BreakerState.ChallengePaused, state);
        var exception = Assert.Throws<AcquisitionException>(() => breaker.ThrowIfOpen(Host));
        Assert.Equal("SNR-ACQ-011", exception.Code);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.NotEqual(BreakerState.Blocked, breaker.GetState(Host));
    }

    [Fact]
    public void A_response_with_no_refusal_signature_never_counts_as_a_strike()
    {
        // ChallengeSeverity.None means "this was not a block at all", so it must not move the circuit
        // even if a caller passes it repeatedly.
        var (breaker, _) = Create(new BreakerOptions(BlockThreshold: 2));

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(BreakerState.Closed, breaker.RecordBlock(Host, ChallengeSeverity.None));
        }

        Assert.Equal(BreakerState.Closed, breaker.GetState(Host));
    }

    [Fact]
    public void Hosts_are_isolated_from_one_another()
    {
        var (breaker, _) = Create(new BreakerOptions(BlockThreshold: 2));

        breaker.RecordBlock(Host, ChallengeSeverity.Generic);
        breaker.RecordBlock(Host, ChallengeSeverity.Generic);

        Assert.Equal(BreakerState.Blocked, breaker.GetState(Host));
        Assert.Equal(BreakerState.Closed, breaker.GetState("other.test"));
        breaker.ThrowIfOpen("other.test");
    }
}
