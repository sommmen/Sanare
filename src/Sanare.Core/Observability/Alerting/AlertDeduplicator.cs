namespace Sanare.Core.Observability.Alerting;

/// <summary>Allows an alert only once for each false-to-true transition per source.</summary>
public sealed class AlertDeduplicator
{
    private readonly Dictionary<(string Rule, string Source), bool> _states = new();

    public bool ShouldRaise(AlertRule rule, string sourceId, bool qualifying)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        var key = (rule.Name, sourceId);
        var previouslyQualifying = _states.TryGetValue(key, out var state) && state;
        _states[key] = qualifying;
        return qualifying && !previouslyQualifying;
    }
}
