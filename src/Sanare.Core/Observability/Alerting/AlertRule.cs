using Sanare.Abstractions.Telemetry;

namespace Sanare.Core.Observability.Alerting;

/// <summary>A named threshold rule with a stable severity.</summary>
public sealed record AlertRule(string Name, AlertSeverity Severity);
