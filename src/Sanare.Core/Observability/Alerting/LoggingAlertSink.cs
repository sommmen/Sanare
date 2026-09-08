using Microsoft.Extensions.Logging;
using Sanare.Abstractions.Telemetry;

namespace Sanare.Core.Observability.Alerting;

/// <summary>Default <see cref="IAlertSink"/> that logs at Critical/Error until a consumer supplies a real sink.</summary>
public sealed class LoggingAlertSink : IAlertSink
{
    private readonly ILogger<LoggingAlertSink> _logger;

    public LoggingAlertSink(ILogger<LoggingAlertSink> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public ValueTask RaiseAsync(AlertRaised alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);
        var level = alert.Severity == AlertSeverity.Critical ? LogLevel.Critical : LogLevel.Error;
        _logger.Log(level, "Alert {AlertName} raised for source {SourceId}: {Detail}", alert.Name, alert.SourceId, alert.Detail);
        return ValueTask.CompletedTask;
    }
}
