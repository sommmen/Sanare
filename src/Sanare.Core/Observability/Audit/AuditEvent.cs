namespace Sanare.Core.Observability.Audit;

/// <summary>An immutable, redacted security-relevant audit event.</summary>
public sealed record AuditEvent(
    string EventType,
    DateTimeOffset Timestamp,
    string? SourceId = null,
    IReadOnlyDictionary<string, object?>? Data = null);
