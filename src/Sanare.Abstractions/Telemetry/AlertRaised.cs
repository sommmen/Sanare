namespace Sanare.Abstractions.Telemetry;

/// <summary>An alert raised by a Sanare operational rule.</summary>
public sealed record AlertRaised(
    string Name,
    AlertSeverity Severity,
    string SourceId,
    DateTimeOffset Timestamp,
    string? Detail = null);
