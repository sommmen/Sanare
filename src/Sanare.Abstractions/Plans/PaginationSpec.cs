namespace Sanare.Abstractions.Plans;

/// <summary>
/// A plan's default pagination behaviour. See docs/sanare/tech-design.md §10.1.1
/// (<c>pagination: { "strategy": "NextLink", "nextSelector": ..., "maxPages": ..., "itemKey": ... }</c>).
/// </summary>
/// <param name="Strategy">
/// The strategy used to discover subsequent pages. Shares <see cref="Abstractions.PaginationStrategy"/>
/// with <see cref="PaginationPolicy"/> (docs/features/extraction-plan-model.md's file tree lists a
/// second <c>PaginationStrategy.cs</c> under <c>Plans/</c>, but the strategy vocabulary is identical in
/// both places — see docs/features/pagination-engine.md "Strategy semantics" — so v0.1 reuses the single
/// existing enum rather than declaring a duplicate type with the same name and members).
/// </param>
/// <param name="NextSelector">Selector (or link relation) used by <see cref="PaginationStrategy.NextLink"/>.</param>
/// <param name="MaxPages">Upper bound on pages fetched; validated into 1–10,000 by <c>IPlanValidator</c>.</param>
/// <param name="ItemKey">
/// A JSON pointer/path identifying the field used to de-duplicate items across pages, e.g. <c>"$.products[*].sku"</c>.
/// </param>
/// <param name="MaxItems">
/// Optional upper bound on total items collected across all pages; validated into 1–1,000,000 by
/// <c>IPlanValidator</c> when set. <see langword="null"/> means no item-count cap.
/// </param>
/// <remarks>
/// No behaviour lives here; interpreting a pagination spec against a live run is <c>pagination-engine</c>
/// scope (M2) and not implemented in v0.1 — only the plan-time shape and its static
/// <see cref="None"/> default exist.
/// </remarks>
public sealed record PaginationSpec(
    PaginationStrategy Strategy,
    string? NextSelector = null,
    int MaxPages = 1,
    string? ItemKey = null,
    int? MaxItems = null)
{
    /// <summary>The default: only the first page is fetched.</summary>
    public static PaginationSpec None { get; } = new(PaginationStrategy.None);
}
