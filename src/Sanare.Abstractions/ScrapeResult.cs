using Sanare.Abstractions.Diagnostics;
using Sanare.Abstractions.Quality;

namespace Sanare.Abstractions;

/// <summary>
/// The result envelope returned by <see cref="IScrapeRunner.RunAsync{TSchema}"/>. <see cref="Payload"/>
/// is non-null exactly when <see cref="Status"/> is <see cref="ScrapeStatus.Succeeded"/>,
/// <see cref="ScrapeStatus.PartialExtraction"/>, or <see cref="ScrapeStatus.PartialPagination"/>; it is
/// null for every other status, most notably a missing required field
/// (<see cref="ScrapeStatus.SchemaValidationFailed"/>) never carries partial data.
/// </summary>
public sealed record ScrapeResult<T>
{
    /// <summary>The terminal outcome of the run.</summary>
    public required ScrapeStatus Status { get; init; }

    /// <summary>The extracted, validated payload, or null when extraction did not succeed.</summary>
    public T? Payload { get; init; }

    /// <summary>Field-level completeness and reliability report.</summary>
    public required QualityReport Quality { get; init; }

    /// <summary>What happened while producing this result (source, tier, origin, timing).</summary>
    public required RunProvenance Provenance { get; init; }

    /// <summary>Ordered diagnostics accumulated during the run.</summary>
    public required IReadOnlyList<ScrapeDiagnostic> Diagnostics { get; init; }

    /// <summary>True only when <see cref="Status"/> is <see cref="ScrapeStatus.Succeeded"/>.</summary>
    public bool IsSuccess => Status is ScrapeStatus.Succeeded;

    /// <summary>True when <see cref="Payload"/> is non-null.</summary>
    public bool HasPayload => Payload is not null;
}
