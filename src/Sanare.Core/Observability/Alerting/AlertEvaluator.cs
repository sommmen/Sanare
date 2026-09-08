using Microsoft.Extensions.Logging;
using Sanare.Abstractions.Telemetry;

namespace Sanare.Core.Observability.Alerting;

/// <summary>Evaluates alert transitions without allowing a sink failure to disrupt a scrape.</summary>
public sealed class AlertEvaluator
{
    private readonly IAlertSink _sink;
    private readonly AlertDeduplicator _deduplicator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AlertEvaluator>? _logger;

    public AlertEvaluator(IAlertSink sink, AlertDeduplicator? deduplicator = null, TimeProvider? timeProvider = null, ILogger<AlertEvaluator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
        _deduplicator = deduplicator ?? new AlertDeduplicator();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async ValueTask EvaluateAsync(AlertRule rule, string sourceId, bool qualifying, string? detail = null, CancellationToken cancellationToken = default)
    {
        if (!_deduplicator.ShouldRaise(rule, sourceId, qualifying))
        {
            return;
        }

        try
        {
            await _sink.RaiseAsync(new AlertRaised(rule.Name, rule.Severity, sourceId, _timeProvider.GetUtcNow(), detail), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning(exception, "SNR-OBS-004: Alert sink failed for {AlertName}.", rule.Name);
        }
    }

    public ValueTask EvaluateBudgetAsync(string sourceId, double spentRatio, CancellationToken cancellationToken = default) =>
        EvaluateBudgetCoreAsync(sourceId, spentRatio, cancellationToken);

    public ValueTask EvaluateStateRootPressureAsync(string sourceId, double freeSpaceRatio, CancellationToken cancellationToken = default) =>
        EvaluateAsync(AlertRuleSet.StateRootPressure, sourceId, freeSpaceRatio < 0.10, cancellationToken: cancellationToken);

    private async ValueTask EvaluateBudgetCoreAsync(string sourceId, double spentRatio, CancellationToken cancellationToken)
    {
        await EvaluateAsync(AlertRuleSet.BudgetNearLimit, sourceId, spentRatio >= 0.8 && spentRatio < 1.0, cancellationToken: cancellationToken).ConfigureAwait(false);
        await EvaluateAsync(AlertRuleSet.BudgetExhausted, sourceId, spentRatio >= 1.0, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
