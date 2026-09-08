namespace Sanare.Abstractions;

/// <summary>
/// A single item yielded by <see cref="IScrapeRunner.StreamAsync{TItem}"/> as pages arrive.
/// </summary>
/// <remarks>
/// Shaped after <c>PagedItem&lt;T&gt;</c> from docs/features/pagination-engine.md, since streaming a
/// <see cref="ScrapeRequest"/> ultimately runs through the pagination engine (M2 scope). The v0.1
/// engine only implements <see cref="IScrapeRunner.RunAsync{TSchema}"/>; <c>StreamAsync</c> exists so
/// the interface's full public surface compiles today, but currently throws
/// <see cref="NotSupportedException"/> — see the engine's TODO for streaming support.
/// </remarks>
/// <param name="Value">The extracted, validated item.</param>
/// <param name="PageNumber">The 1-based page the item was found on.</param>
/// <param name="PageUrl">The URL of the page the item was found on.</param>
/// <param name="Observations">Per-field observations for this item.</param>
public sealed record ScrapeItem<T>(
    T Value,
    int PageNumber,
    string PageUrl,
    IReadOnlyList<FieldObservation> Observations);
