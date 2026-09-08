namespace Sanare.Abstractions;

/// <summary>
/// Pure mapping from <see cref="ScrapeStatus"/> to the error codes a consumer can expect to see
/// alongside it, matching docs/sanare/tech-design.md §9.2.5 exactly so the mapping is testable and
/// cannot silently drift from the documented table.
/// </summary>
public static class ScrapeStatusCodes
{
    /// <summary>
    /// Returns the ordered, distinct set of codes typically associated with <paramref name="status"/>.
    /// An empty array means the status alone is the signal (no accompanying code is expected).
    /// </summary>
    public static IReadOnlyList<string> For(ScrapeStatus status) => status switch
    {
        ScrapeStatus.InvalidRequest =>
            ["SNR-API-001", "SNR-API-002", "SNR-API-004", "SNR-API-005"],
        ScrapeStatus.PartialExtraction => ["SNR-EXT-003"],
        ScrapeStatus.SchemaValidationFailed => ["SNR-SCH-002", "SNR-SCH-004", "SNR-SCH-005"],
        ScrapeStatus.NoPlanAvailable => [],
        ScrapeStatus.AwaitingApproval => [],
        ScrapeStatus.AuthoringFailed => ["SNR-AUTH-002", "SNR-AUTH-003"],
        ScrapeStatus.PlanInvalid => ["SNR-PLAN-001", "SNR-PLAN-002", "SNR-PLAN-003"],
        ScrapeStatus.ExtractionFailed => ["SNR-SCH-002"],
        ScrapeStatus.PaginationCapReached => ["SNR-PAG-005"],
        ScrapeStatus.PartialPagination => ["SNR-PAG-005"],
        ScrapeStatus.RateLimited => ["SNR-ACQ-002"],
        ScrapeStatus.Blocked => ["SNR-ACQ-003"],
        ScrapeStatus.DisallowedByRobots => ["SNR-ACQ-004"],
        ScrapeStatus.ConsentWallBlocked => ["SNR-ACQ-005"],
        ScrapeStatus.BrowserFailed => ["SNR-BRW-003", "SNR-BRW-004", "SNR-BRW-005"],
        ScrapeStatus.FixtureNotFound => ["SNR-FIX-001"],
        ScrapeStatus.SourceNotFound => ["SNR-API-002"],
        ScrapeStatus.Timeout => ["SNR-ACQ-001"],
        ScrapeStatus.Cancelled => [],
        ScrapeStatus.Succeeded => [],
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, message: null),
    };
}
