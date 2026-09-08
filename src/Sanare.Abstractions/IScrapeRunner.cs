namespace Sanare.Abstractions;

/// <summary>
/// The primary execution interface of the engine: turn a <see cref="ScrapeRequest"/> into a typed,
/// quality-scored result. See docs/sanare/tech-design.md §9.2.1.
/// </summary>
public interface IScrapeRunner
{
    /// <summary>
    /// Materialises the whole result, including all pages, respecting <see cref="ScrapeRequest.MaxItems"/>
    /// and the pagination policy's page cap. This is the default, memory-simple path.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> is null. This is the one case the engine throws rather than reporting
    /// as a status/diagnostic pair.
    /// </exception>
    Task<ScrapeResult<TSchema>> RunAsync<TSchema>(
        ScrapeRequest request,
        CancellationToken cancellationToken = default)
        where TSchema : class;

    /// <summary>
    /// Yields items as pages are enumerated; stopping enumeration stops fetching. This is the
    /// memory-bounded path for large listers. Terminal failures are surfaced by throwing
    /// <see cref="ScrapeStreamException"/>, since an <see cref="IAsyncEnumerable{T}"/> has no envelope
    /// to carry a status in.
    /// </summary>
    /// <remarks>
    /// Not implemented in v0.1: streaming requires the pagination engine (M2 scope). The v0.1 engine's
    /// implementation throws <see cref="NotSupportedException"/>.
    /// </remarks>
    IAsyncEnumerable<ScrapeItem<TItem>> StreamAsync<TItem>(
        ScrapeRequest request,
        CancellationToken cancellationToken = default)
        where TItem : class;
}
