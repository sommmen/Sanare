namespace Sanare.Abstractions;

/// <summary>
/// Closed, additive-only set of terminal outcomes for a scrape run.
/// Consumers should switch exhaustively over this enum; new values are only ever appended.
/// </summary>
public enum ScrapeStatus
{
    /// <summary>All required fields were extracted and validated successfully.</summary>
    Succeeded,

    /// <summary>The run produced a payload but one or more optional fields or items are missing.</summary>
    PartialExtraction,

    /// <summary>The <see cref="ScrapeRequest"/> failed normalization/validation before execution began.</summary>
    InvalidRequest,

    /// <summary>No extraction plan exists for the requested source and authoring was not allowed or failed.</summary>
    NoPlanAvailable,

    /// <summary>A newly authored plan requires human approval before it can be used.</summary>
    AwaitingApproval,

    /// <summary>Plan authoring was attempted and failed.</summary>
    AuthoringFailed,

    /// <summary>The extracted document failed schema validation (missing required field or failed coercion).</summary>
    SchemaValidationFailed,

    /// <summary>The extraction plan itself is invalid (unsupported version or operation).</summary>
    PlanInvalid,

    /// <summary>Extraction failed due to an unexpected runtime error or exceeded budget.</summary>
    ExtractionFailed,

    /// <summary>Pagination stopped because the configured item/page cap was reached.</summary>
    PaginationCapReached,

    /// <summary>Pagination stopped early but at least one page was successfully processed.</summary>
    PartialPagination,

    /// <summary>The source rate-limited the request.</summary>
    RateLimited,

    /// <summary>The source blocked the request (e.g. anti-bot challenge).</summary>
    Blocked,

    /// <summary>The request target is disallowed by robots.txt.</summary>
    DisallowedByRobots,

    /// <summary>A consent wall blocked access to the requested content.</summary>
    ConsentWallBlocked,

    /// <summary>Browser-tier acquisition failed.</summary>
    BrowserFailed,

    /// <summary>The requested fixture could not be found (offline mode).</summary>
    FixtureNotFound,

    /// <summary>The requested source id is not registered.</summary>
    SourceNotFound,

    /// <summary>The run exceeded its configured time budget.</summary>
    Timeout,

    /// <summary>The run was cancelled via the supplied <see cref="System.Threading.CancellationToken"/>.</summary>
    Cancelled,
}
