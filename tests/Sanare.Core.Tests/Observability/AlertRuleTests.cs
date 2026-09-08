using Sanare.Abstractions.Telemetry;
using Sanare.Core.Observability.Alerting;

namespace Sanare.Core.Tests.Observability;

public sealed class AlertRuleTests
{
    [Fact]
    public async Task EvaluateAsync_Raises_an_alert_exactly_once_per_qualifying_transition_not_per_run()
    {
        // AC-OB-013: repeated qualifying runs must not re-raise the same alert.
        var sink = new SpyAlertSink();
        var evaluator = new AlertEvaluator(sink, timeProvider: new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        await evaluator.EvaluateAsync(AlertRuleSet.SourceBlocked, "source-a", qualifying: true);
        await evaluator.EvaluateAsync(AlertRuleSet.SourceBlocked, "source-a", qualifying: true);
        await evaluator.EvaluateAsync(AlertRuleSet.SourceBlocked, "source-a", qualifying: true);

        Assert.Single(sink.Raised);
    }

    [Fact]
    public async Task EvaluateAsync_Raises_again_after_a_false_to_true_transition()
    {
        var sink = new SpyAlertSink();
        var evaluator = new AlertEvaluator(sink, timeProvider: new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        await evaluator.EvaluateAsync(AlertRuleSet.SourceBlocked, "source-a", qualifying: true);
        await evaluator.EvaluateAsync(AlertRuleSet.SourceBlocked, "source-a", qualifying: false);
        await evaluator.EvaluateAsync(AlertRuleSet.SourceBlocked, "source-a", qualifying: true);

        Assert.Equal(2, sink.Raised.Count);
    }

    [Fact]
    public async Task EvaluateAsync_Tracks_deduplication_state_independently_per_source()
    {
        var sink = new SpyAlertSink();
        var evaluator = new AlertEvaluator(sink, timeProvider: new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        await evaluator.EvaluateAsync(AlertRuleSet.SourceBlocked, "source-a", qualifying: true);
        await evaluator.EvaluateAsync(AlertRuleSet.SourceBlocked, "source-b", qualifying: true);

        Assert.Equal(2, sink.Raised.Count);
        Assert.Contains(sink.Raised, alert => alert.SourceId == "source-a");
        Assert.Contains(sink.Raised, alert => alert.SourceId == "source-b");
    }

    [Theory]
    [InlineData(0.09, true)]
    [InlineData(0.10, false)]
    [InlineData(0.11, false)]
    public async Task EvaluateStateRootPressureAsync_Raises_StateRootPressure_only_below_10_percent_free(double freeSpaceRatio, bool expectRaised)
    {
        // AC-OB-014: StateRootPressure at 9 % / 10 % / 11 % free space.
        var sink = new SpyAlertSink();
        var evaluator = new AlertEvaluator(sink, timeProvider: new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        await evaluator.EvaluateStateRootPressureAsync("source-a", freeSpaceRatio);

        Assert.Equal(expectRaised, sink.Raised.Count == 1);
        if (expectRaised)
        {
            Assert.Equal("StateRootPressure", Assert.Single(sink.Raised).Name);
        }
    }

    [Fact]
    public async Task EvaluateBudgetAsync_Raises_BudgetNearLimit_between_80_and_100_percent_spent()
    {
        var sink = new SpyAlertSink();
        var evaluator = new AlertEvaluator(sink, timeProvider: new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        await evaluator.EvaluateBudgetAsync("source-a", 0.85);

        Assert.Equal("BudgetNearLimit", Assert.Single(sink.Raised).Name);
    }

    [Fact]
    public async Task EvaluateBudgetAsync_Raises_BudgetExhausted_at_or_above_100_percent_spent()
    {
        var sink = new SpyAlertSink();
        var evaluator = new AlertEvaluator(sink, timeProvider: new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        await evaluator.EvaluateBudgetAsync("source-a", 1.0);

        Assert.Equal("BudgetExhausted", Assert.Single(sink.Raised).Name);
    }

    [Fact]
    public async Task EvaluateAsync_Swallows_a_sink_failure_so_a_scrape_is_never_disrupted()
    {
        var sink = new ThrowingAlertSink();
        var evaluator = new AlertEvaluator(sink, timeProvider: new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        var exception = await Record.ExceptionAsync(
            async () => await evaluator.EvaluateAsync(AlertRuleSet.SourceBlocked, "source-a", qualifying: true));

        Assert.Null(exception);
    }

    [Fact]
    public async Task EvaluateAsync_Includes_the_configured_severity_on_the_raised_alert()
    {
        var sink = new SpyAlertSink();
        var evaluator = new AlertEvaluator(sink, timeProvider: new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        await evaluator.EvaluateAsync(AlertRuleSet.SourceBlocked, "source-a", qualifying: true);

        Assert.Equal(AlertSeverity.Critical, Assert.Single(sink.Raised).Severity);
    }

    [Fact]
    public void AlertDeduplicator_ShouldRaise_Returns_true_only_on_a_false_to_true_transition()
    {
        var deduplicator = new AlertDeduplicator();

        Assert.True(deduplicator.ShouldRaise(AlertRuleSet.FieldDecay, "source-a", qualifying: true));
        Assert.False(deduplicator.ShouldRaise(AlertRuleSet.FieldDecay, "source-a", qualifying: true));
        Assert.False(deduplicator.ShouldRaise(AlertRuleSet.FieldDecay, "source-a", qualifying: false));
        Assert.True(deduplicator.ShouldRaise(AlertRuleSet.FieldDecay, "source-a", qualifying: true));
    }

    private sealed class SpyAlertSink : IAlertSink
    {
        public List<AlertRaised> Raised { get; } = [];

        public ValueTask RaiseAsync(AlertRaised alert, CancellationToken cancellationToken = default)
        {
            Raised.Add(alert);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAlertSink : IAlertSink
    {
        public ValueTask RaiseAsync(AlertRaised alert, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated sink failure.");
    }
}
