namespace Sanare.Abstractions;

/// <summary>
/// Strategy used to discover the next page of a paginated listing. See
/// docs/features/pagination-engine.md ("Strategy semantics") for full behaviour of each value.
/// Only the type shape is owned by v0.1; the pagination engine that interprets these values is
/// M2 scope (feature: pagination-engine) and is not implemented yet.
/// </summary>
public enum PaginationStrategy
{
    /// <summary>No pagination; only the first page is fetched.</summary>
    None,

    /// <summary>Follow a "next" link (rel=next or a declared selector).</summary>
    NextLink,

    /// <summary>Increment a page-number parameter in a URL template.</summary>
    PageNumber,

    /// <summary>Increment an offset/limit parameter pair in a URL template.</summary>
    Offset,

    /// <summary>Follow an opaque cursor token found in the payload.</summary>
    Cursor,

    /// <summary>Click a "load more" button (browser tier only).</summary>
    LoadMoreButton,

    /// <summary>Scroll to load more items (browser tier only).</summary>
    InfiniteScroll,
}
