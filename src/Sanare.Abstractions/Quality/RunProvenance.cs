namespace Sanare.Abstractions.Quality;

/// <summary>
/// Records exactly what happened during a run: which source, which plan, which acquisition tier,
/// where the content came from, and timing/fixture bookkeeping. This is the "typed-but-dynamic"
/// half of a <see cref="ScrapeResult{T}"/> — everything that cannot be expressed in the compile-time
/// schema type. See docs/sanare/tech-design.md §9.2.1.
/// </summary>
public sealed record RunProvenance
{
    /// <summary>Unique identifier for this run.</summary>
    public required string RunId { get; init; }

    /// <summary>The (possibly derived) source id the run executed against.</summary>
    public required string SourceId { get; init; }

    /// <summary>Commit id of the plan used, when a version-controlled plan served the run.</summary>
    public string? PlanCommitId { get; init; }

    /// <summary>Content hash of the schema the run validated against.</summary>
    public required string SchemaHash { get; init; }

    /// <summary>The acquisition tier that produced the content.</summary>
    public required AcquisitionTier Tier { get; init; }

    /// <summary>Number of pages fetched (1 for a non-paginated run).</summary>
    public required int PagesFetched { get; init; }

    /// <summary>Number of network requests issued (0 when served entirely from cache or fixture).</summary>
    public required int RequestsIssued { get; init; }

    /// <summary>Where the content ultimately came from.</summary>
    public required ResultOrigin Origin { get; init; }

    /// <summary>Whether the run was served from either cache tier.</summary>
    public bool ServedFromCache => Origin is ResultOrigin.HttpCache or ResultOrigin.ResultCache;

    /// <summary>Whether the run was served entirely from a recorded fixture (no network access).</summary>
    public bool ServedFromFixture => Origin == ResultOrigin.Fixture;

    /// <summary>When the run started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Total wall-clock duration of the run.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Ids of any fixtures consulted while producing this result.</summary>
    public required IReadOnlyList<string> FixtureIds { get; init; }
}
