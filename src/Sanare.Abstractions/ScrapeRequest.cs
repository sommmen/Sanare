namespace Sanare.Abstractions;

/// <summary>
/// Describes a single scrape invocation. See docs/sanare/tech-design.md §9.2.1 and
/// docs/features/scrape-api-contracts.md for the normalisation rules applied before execution.
/// </summary>
public sealed record ScrapeRequest
{
    /// <summary>
    /// The target URL. Must be absolute, use scheme <c>http</c> or <c>https</c>, have a non-empty host,
    /// no userinfo component, and be no longer than 2048 characters (<c>SNR-API-001</c> otherwise).
    /// </summary>
    public required Uri Url { get; init; }

    /// <summary>
    /// Optional source id matching <c>^[a-z0-9-]+(/[a-z0-9-]+)*$</c>, at most 128 characters
    /// (<c>SNR-API-002</c> otherwise). When omitted it is derived deterministically as
    /// <c>{host-slug}/{first-path-segment-slug}</c>.
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>Maximum acceptable age for a cached result.</summary>
    public TimeSpan? Freshness { get; init; }

    /// <summary>
    /// Maximum number of items to return for a collection schema. Clamped into 1…1,000,000
    /// with a <c>SNR-API-003</c> warning when out of range.
    /// </summary>
    public int? MaxItems { get; init; }

    /// <summary>Overrides the plan's default pagination policy.</summary>
    public PaginationPolicy? Pagination { get; init; }

    /// <summary>Source culture override for coercion, e.g. <c>nl-NL</c>.</summary>
    public string? Culture { get; init; }

    /// <summary>Whether plan authoring may run when no approved plan exists for this request.</summary>
    public bool AllowAuthoring { get; init; } = true;

    /// <summary>Template variables substituted into the plan's parameterised locators/URLs.</summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>Pins execution to an exact plan commit rather than the latest approved plan.</summary>
    public string? PlanCommitId { get; init; }
}
