namespace Sanare.Abstractions.Plans;

/// <summary>
/// Describes how to fetch the content a plan extracts from. See docs/features/extraction-plan-model.md
/// ("Object model") and docs/sanare/tech-design.md §10.1.1.
/// </summary>
/// <param name="Method">The HTTP method used to fetch <paramref name="UrlTemplate"/>.</param>
/// <param name="UrlTemplate">
/// An absolute <c>http</c>/<c>https</c> URL, optionally containing <c>{placeholder}</c> tokens bound from
/// the request's parameter set (e.g. <c>{page}</c>) or from <see cref="ScrapeRequest.Parameters"/>.
/// Validated by <c>IPlanValidator</c> (docs/features/extraction-plan-model.md, validation rule 8).
/// </param>
/// <param name="Headers">
/// Additional request headers. Must never contain <c>Cookie</c>, <c>Authorization</c>, or
/// <c>Set-Cookie</c> (validation rule 9) — credentials never live in a plan and therefore never reach git.
/// </param>
/// <param name="WaitFor">A browser-tier-only selector to await before extraction begins; <see langword="null"/> otherwise.</param>
/// <param name="Interactions">Browser-tier-only interaction steps executed before extraction begins; empty for other tiers.</param>
/// <remarks>No behaviour lives here; issuing the request is <c>acquisition-pipeline</c> scope (M2).</remarks>
public sealed record AcquisitionSpec(
    AcquisitionMethod Method,
    string UrlTemplate,
    IReadOnlyDictionary<string, string> Headers,
    string? WaitFor,
    IReadOnlyList<InteractionStep> Interactions);
