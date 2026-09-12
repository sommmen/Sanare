namespace Sanare.Abstractions.Plans;

/// <summary>
/// The artefact an LLM (or a human, for the v0.1 fixtures) produces and the deterministic runtime
/// consumes: everything required to fetch a source and map its content onto a schema. See
/// docs/features/extraction-plan-model.md ("Object model") and docs/sanare/tech-design.md §10.1.1 for
/// the canonical JSON shape this type mirrors.
/// </summary>
/// <remarks>
/// <para>
/// This is the security boundary of the whole system (DR-001): every locator and transform is a
/// <see cref="PlanOperation"/> drawn from a closed vocabulary, so a plan can never cause arbitrary code
/// to execute. Records here are inert data — "no behaviour in the model" (constraint in
/// docs/features/extraction-plan-model.md). Structural validation is available via
/// <c>Sanare.Core.Plans.IPlanValidator</c>; execution via <c>plan-runtime</c> belongs to the full
/// <c>plan-runtime</c> feature and is not implemented in v0.1 — the narrow
/// <see cref="Sanare.Core.PlanExecutor"/> that does exist trusts its hand-authored, in-process plans and
/// rejects unsupported operations at run time instead.
/// </para>
/// <para>A plan object graph is never mutated after construction; every collection here is read-only.</para>
/// </remarks>
public sealed record ExtractionPlan
{
    /// <summary>
    /// The current plan vocabulary version. Version 2 replaced each field's <c>locators[]</c> array with
    /// the named <see cref="FieldPlan.PrimaryLocator"/>/<see cref="FieldPlan.FallbackLocator"/> pair
    /// (DR-002).
    /// </summary>
    public const int CurrentPlanVersion = 2;

    /// <summary>
    /// The oldest <see cref="PlanVersion"/> <c>Sanare.Core.Plans.PlanSerializer</c> can still read via its
    /// in-memory upgrade path (DR-002's <c>N-1</c> window). A plan below this fails with
    /// <c>SNR-PLAN-002</c>.
    /// </summary>
    public const int MinimumReadablePlanVersion = 1;

    /// <summary>
    /// The plan vocabulary's major version. Runtime declares a <c>CurrentPlanVersion</c> and reads down to
    /// <c>CurrentPlanVersion - 1</c> via a registered upgrade function (DR-002); anything outside that
    /// window fails <c>SNR-PLAN-002</c>. Version handling itself is <c>Sanare.Core</c> scope.
    /// </summary>
    public required int PlanVersion { get; init; }

    /// <summary>The source this plan targets, matching a normalised <see cref="ScrapeRequest.SourceId"/>.</summary>
    public required string SourceId { get; init; }

    /// <summary>The name of the schema type this plan populates, e.g. <c>"TabletListing"</c>.</summary>
    public required string SchemaName { get; init; }

    /// <summary>The schema's version, matching the schema type's derived <c>SchemaVersion</c>.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>
    /// The content hash of the schema this plan was authored/validated against
    /// (see <c>SchemaDescriptor.SchemaHash</c> in <c>schema-engine</c>); validated for equality by
    /// <c>IPlanValidator</c> when a schema is supplied.
    /// </summary>
    public required string SchemaHash { get; init; }

    /// <summary>The culture used for coercion, e.g. <c>"nl-NL"</c>; never taken from the ambient thread culture.</summary>
    public required string Culture { get; init; }

    /// <summary>The acquisition tier this plan was authored for; determines which operations are legal.</summary>
    public required AcquisitionTier Tier { get; init; }

    /// <summary>How to fetch the content this plan extracts from.</summary>
    public required AcquisitionSpec Acquisition { get; init; }

    /// <summary>
    /// A predicate that, when matched, means the resource does not exist rather than that extraction
    /// failed. Evaluated before any field is extracted. <see langword="null"/> when the source never
    /// signals absence this way.
    /// </summary>
    public NotFoundSpec? NotFound { get; init; }

    /// <summary>The consent/cookie-wall handling this plan expects. <see langword="null"/> when the source has none.</summary>
    public ConsentSpec? Consent { get; init; }

    /// <summary>The default pagination behaviour; <see cref="PaginationSpec.None"/> (first page only) unless overridden.</summary>
    public PaginationSpec Pagination { get; init; } = PaginationSpec.None;

    /// <summary>
    /// The root locator step selecting each item in a collection schema (e.g. <c>{"op":"selectAll", ...}</c>).
    /// <see langword="null"/> for a single-item schema.
    /// </summary>
    public string? Root { get; init; }

    /// <summary>Every field's extraction recipe. Must cover every required schema field; pointers must be unique.</summary>
    public required IReadOnlyList<FieldPlan> Fields { get; init; }

    /// <summary>How this plan came to exist.</summary>
    public required PlanProvenance Provenance { get; init; }
}
