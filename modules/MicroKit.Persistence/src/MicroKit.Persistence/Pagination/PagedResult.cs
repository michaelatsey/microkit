namespace MicroKit.Persistence;

/// <summary>
/// Concrete implementation of <see cref="IPagedResult{T}"/> returned by
/// <c>IReadRepository&lt;TAggregate&gt;.ListPagedAsync</c>.
/// </summary>
/// <typeparam name="T">The type of items on the current page.</typeparam>
/// <param name="Items">The items on the current page.</param>
/// <param name="TotalCount">The total number of items across all pages.</param>
/// <param name="Page">The one-based current page number.</param>
/// <param name="PageSize">The maximum number of items per page.</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize) : IPagedResult<T>
{
    /// <inheritdoc/>
    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling((double)TotalCount / PageSize)
        : 0;

    /// <inheritdoc/>
    public bool HasNextPage => Page < TotalPages;

    /// <inheritdoc/>
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    /// Returns an empty <see cref="PagedResult{T}"/> for the given page and page size.
    /// </summary>
    /// <param name="page">The one-based page number.</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns>A result with no items and a total count of zero.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1000:Do not declare static members on generic types",
        Justification = "PagedResult<T>.Empty() is a well-established factory pattern that requires the type argument at the call site by design.")]
    public static PagedResult<T> Empty(int page, int pageSize) =>
        new([], TotalCount: 0, page, pageSize);
}
