using Microsoft.Extensions.Logging;

namespace Sanare.Core.Observability.Redaction;

/// <summary>Wraps a logger so redaction is complete before the downstream provider receives state.</summary>
public sealed class RedactingLogEnricher : ILogger
{
    private readonly ILogger _inner;
    private readonly RedactionPolicy _policy;

    public RedactingLogEnricher(ILogger inner, RedactionPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _policy = policy ?? new RedactionPolicy();
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
        _inner.BeginScope(RedactState(state));

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        var redacted = RedactState(state);
        _inner.Log(logLevel, eventId, redacted, exception, static (value, _) => value.ToString() ?? string.Empty);
    }

    private object RedactState<TState>(TState state)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            return pairs.Select(pair => new KeyValuePair<string, object?>(pair.Key, _policy.RedactValue(pair.Key, pair.Value))).ToArray();
        }

        return _policy.Redact(state?.ToString() ?? string.Empty);
    }
}
