using Sanare.Abstractions.Diagnostics;

namespace Sanare.Core.Runtime;

/// <summary>Raw deterministic result of executing a v0.1 plan.</summary>
public sealed record ExtractionOutcome(
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<ScrapeDiagnostic> Diagnostics,
    IReadOnlySet<string> CoercionFailedFields,
    bool RequiredFieldsPresent);
