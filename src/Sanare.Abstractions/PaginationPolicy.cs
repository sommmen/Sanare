namespace Sanare.Abstractions;

/// <summary>
/// Overrides the pagination behaviour a plan would otherwise use by default.
/// </summary>
/// <remarks>
/// This is a deliberately minimal placeholder: it exists only so that <see cref="ScrapeRequest"/> and
/// <see cref="IScrapeRunner"/> compile as the shared v0.1 contract surface. The pagination engine that
/// interprets it — strategy-specific factory methods (<c>NextLink</c>, <c>PageNumber</c>, ...), cap
/// clamping (<c>SNR-PAG-005</c>), and de-duplication — is out of scope for the v0.1 milestone and is
/// owned by the <c>pagination-engine</c> feature (docs/features/pagination-engine.md). Do not add
/// behaviour here; extend this record when that feature is implemented.
/// </remarks>
public sealed record PaginationPolicy
{
    /// <summary>Strategy used to discover subsequent pages.</summary>
    public required PaginationStrategy Strategy { get; init; }

    /// <summary>Maximum number of pages to fetch. Clamped into 1…10,000, default 100.</summary>
    public int MaxPages { get; init; } = 100;

    /// <summary>Minimum delay observed between page fetches. Defaults to 750 ms.</summary>
    public TimeSpan InterPageDelay { get; init; } = TimeSpan.FromMilliseconds(750);
}
