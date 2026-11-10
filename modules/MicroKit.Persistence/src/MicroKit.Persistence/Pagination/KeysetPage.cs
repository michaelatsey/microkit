namespace MicroKit.Persistence;

/// <summary>A page of a keyset (cursor) pagination.</summary>
/// <typeparam name="T">The item type: the aggregate, or the projection.</typeparam>
/// <param name="Items">The items of the page, in the keyset order.</param>
/// <param name="PageSize">The page size the page was read with.</param>
/// <param name="Next">The cursor of the next page; <see langword="null"/> on the last page.</param>
public sealed record KeysetPage<T>(IReadOnlyList<T> Items, int PageSize, KeysetCursor? Next)
{
    /// <summary>Gets a value indicating whether a page follows this one.</summary>
    public bool HasNextPage => Next is not null;
}
