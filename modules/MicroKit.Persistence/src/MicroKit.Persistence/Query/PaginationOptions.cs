namespace MicroKit.Persistence;

/// <summary>
/// Encapsulates the page number and page size for a paginated query.
/// </summary>
/// <param name="Page">
/// The one-based current page number. Must be greater than or equal to 1.
/// </param>
/// <param name="PageSize">
/// The maximum number of items per page. Must be greater than or equal to 1.
/// </param>
public sealed record PaginationOptions(int Page, int PageSize)
{
    /// <summary>
    /// Gets the number of items to skip to reach <see cref="Page"/>.
    /// Computed as <c>(<see cref="Page"/> - 1) * <see cref="PageSize"/></c>.
    /// </summary>
    public int Skip => (Page - 1) * PageSize;
}
