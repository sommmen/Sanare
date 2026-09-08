namespace Sanare.Abstractions.Diagnostics;

/// <summary>
/// Severity of a <see cref="ScrapeDiagnostic"/>.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational; does not affect the run's outcome.</summary>
    Info,

    /// <summary>A condition was tolerated (e.g. a clamp) but is worth surfacing.</summary>
    Warning,

    /// <summary>A condition that caused (or contributed to) a non-success status.</summary>
    Error,
}
