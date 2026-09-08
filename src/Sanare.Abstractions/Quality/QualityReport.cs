namespace Sanare.Abstractions.Quality;

/// <summary>
/// Summarises how completely and reliably a run's payload was extracted. See
/// docs/sanare/tech-design.md §7.5 for the completeness weighting.
/// </summary>
public sealed record QualityReport
{
    /// <summary>Weighted completeness of the payload, in the range 0..1.</summary>
    public required double Completeness { get; init; }

    /// <summary>Per-field health observations.</summary>
    public required IReadOnlyList<FieldHealth> Fields { get; init; }

    /// <summary>Fields present in the source document that could not be mapped to the schema.</summary>
    public required IReadOnlyList<string> UnmappedFields { get; init; }

    /// <summary>Number of items produced (1 for a single-document schema).</summary>
    public required int ItemCount { get; init; }

    /// <summary>Whether the run met the configured quality threshold.</summary>
    public required bool MeetsThreshold { get; init; }
}
