namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Represents a paginated read result containing a page of items and
/// metadata about the full result set.
/// </summary>
/// <typeparam name="T">The type of items on the current page.</typeparam>
/// <remarks>
/// Returned by <c>IReadRepository&lt;TAggregate&gt;.ListPagedAsync</c> in
/// <c>MicroKit.Persistence</c> (Core). The concrete implementation
/// <c>PagedResult&lt;T&gt;</c> also lives in Core.
/// </remarks>
public interface IPagedResult<out T>
{
    /// <summary>
    /// Gets the items on the current page.
    /// </summary>
    IReadOnlyList<T> Items { get; }

    /// <summary>
    /// Gets the total number of items across all pages (not just this page).
    /// </summary>
    int TotalCount { get; }

    /// <summary>
    /// Gets the one-based current page number.
    /// </summary>
    int Page { get; }

    /// <summary>
    /// Gets the maximum number of items per page.
    /// </summary>
    int PageSize { get; }

    /// <summary>
    /// Gets the total number of pages, computed from
    /// <see cref="TotalCount"/> and <see cref="PageSize"/>.
    /// </summary>
    int TotalPages { get; }

    /// <summary>
    /// Gets a value indicating whether a next page exists.
    /// </summary>
    bool HasNextPage { get; }

    /// <summary>
    /// Gets a value indicating whether a previous page exists.
    /// </summary>
    bool HasPreviousPage { get; }
}
