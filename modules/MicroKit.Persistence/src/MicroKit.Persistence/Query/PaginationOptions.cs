namespace MicroKit.Persistence;

/// <summary>
/// Encapsulates the page number and page size for a paginated query.
/// </summary>
/// <remarks>
/// Both values are validated on construction, and the properties are get-only, so a record
/// <c>with</c> expression cannot produce an invalid instance. The maximum page size is not a
/// property of the request: the repository enforces the configured <see cref="PaginationLimits"/>.
/// </remarks>
public sealed record PaginationOptions
{
    /// <summary>
    /// Initializes a new <see cref="PaginationOptions"/>.
    /// </summary>
    /// <param name="page">The one-based page number. Must be greater than or equal to 1.</param>
    /// <param name="pageSize">The maximum number of items per page. Must be greater than or equal to 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="page"/> or <paramref name="pageSize"/> is less than 1, or the number of items
    /// to skip, <c>(page - 1) * pageSize</c>, exceeds <see cref="int.MaxValue"/>.
    /// </exception>
    public PaginationOptions(int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page), page, $"Page {page} of size {pageSize} starts beyond the last addressable item.");
        }

        Page = page;
        PageSize = pageSize;
        Skip = (int)skip;
    }

    /// <summary>
    /// Gets the one-based current page number.
    /// </summary>
    public int Page { get; }

    /// <summary>
    /// Gets the maximum number of items per page.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// Gets the number of items to skip to reach <see cref="Page"/>:
    /// <c>(<see cref="Page"/> - 1) * <see cref="PageSize"/></c>.
    /// </summary>
    public int Skip { get; }
}
