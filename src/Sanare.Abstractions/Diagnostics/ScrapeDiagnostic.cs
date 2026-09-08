namespace Sanare.Abstractions.Diagnostics;

/// <summary>
/// A single observation attached to a <see cref="ScrapeResult{T}"/>. Every diagnostic carries a stable
/// <c>SNR-{AREA}-{nnn}</c> <see cref="Code"/> from the error catalog (see docs/sanare/tech-design.md §7.7)
/// so consumers and alerting can key off it rather than parsing free text.
/// </summary>
/// <param name="Code">Stable error code, e.g. <c>SNR-API-001</c>. Never free text.</param>
/// <param name="Severity">Severity of the observation.</param>
/// <param name="Message">Sanitised, human-readable message. Never contains secrets or credentials.</param>
/// <param name="JsonPointer">Optional JSON pointer into the payload the diagnostic relates to.</param>
/// <param name="Detail">Optional unsanitised context, only populated when diagnostic-detail is enabled.</param>
public sealed record ScrapeDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? JsonPointer = null,
    string? Detail = null);
