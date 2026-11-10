namespace MicroKit.Persistence;

/// <summary>
/// The page sizes a read repository allows: the page it returns when a paged query carries no
/// pagination, and the largest page a caller may request.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton by <c>AddEntityFrameworkCore()</c> with the <see cref="Default"/>
/// values, and replaced through <c>EfCoreBuilder.ConfigurePagination(...)</c>. The in-memory
/// doubles of <c>MicroKit.Persistence.Testing</c> apply the same rules.
/// </para>
/// <para>
/// A page size above <see cref="MaxPageSize"/> is rejected, never silently clamped: a clamped
/// page would return fewer rows than the caller asked for without saying so.
/// </para>
/// </remarks>
public sealed record PaginationLimits
{
    private const int StandardDefaultPageSize = 50;
    private const int StandardMaxPageSize = 500;

    /// <summary>
    /// Initializes a new <see cref="PaginationLimits"/>.
    /// </summary>
    /// <param name="defaultPageSize">
    /// The size of the page returned when a paged query carries no pagination. Must be at least 1
    /// and at most <paramref name="maxPageSize"/>.
    /// </param>
    /// <param name="maxPageSize">The largest page size a caller may request. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="defaultPageSize"/> is less than 1, <paramref name="maxPageSize"/> is less
    /// than 1, or <paramref name="defaultPageSize"/> is greater than <paramref name="maxPageSize"/>.
    /// </exception>
    public PaginationLimits(int defaultPageSize = StandardDefaultPageSize, int maxPageSize = StandardMaxPageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultPageSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(defaultPageSize, maxPageSize);

        DefaultPageSize = defaultPageSize;
        MaxPageSize = maxPageSize;
    }

    /// <summary>
    /// Gets the limits applied when none are configured: a default page of 50 items and a
    /// maximum page size of 500.
    /// </summary>
    public static PaginationLimits Default { get; } = new();

    /// <summary>Gets the size of the page returned when a paged query carries no pagination.</summary>
    public int DefaultPageSize { get; }

    /// <summary>Gets the largest page size a caller may request.</summary>
    public int MaxPageSize { get; }

    /// <summary>
    /// Returns the pagination a paged query runs with: <paramref name="requested"/> when set,
    /// otherwise the first page of <see cref="DefaultPageSize"/> items.
    /// </summary>
    /// <param name="requested">The pagination the query carries, if any.</param>
    /// <returns>The pagination to apply.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="requested"/> asks for more than <see cref="MaxPageSize"/> items.
    /// </exception>
    public PaginationOptions ForPagedQuery(PaginationOptions? requested)
    {
        if (requested is null)
            return new PaginationOptions(1, DefaultPageSize);

        EnsureWithinMaximum(requested);
        return requested;
    }

    /// <summary>Returns the page size of a keyset page: <paramref name="requested"/>, or <see cref="DefaultPageSize"/>.</summary>
    /// <param name="requested">The keyset's <see cref="KeysetPagination{TAggregate}.PageSize"/>, if any.</param>
    /// <returns>The page size to read.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="requested"/> exceeds <see cref="MaxPageSize"/>.</exception>
    public int ForKeysetQuery(int? requested)
    {
        if (requested is not { } size)
            return DefaultPageSize;
        if (size > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested), size,
                $"The page size {size} exceeds the maximum of {MaxPageSize} configured by {nameof(PaginationLimits)}.");
        }

        return size;
    }

    /// <summary>
    /// Rejects <paramref name="requested"/> when it asks for more than <see cref="MaxPageSize"/> items.
    /// </summary>
    /// <param name="requested">The pagination to check; <see langword="null"/> (no pagination) passes.</param>
    /// <exception cref="ArgumentOutOfRangeException">The page size exceeds <see cref="MaxPageSize"/>.</exception>
    public void EnsureWithinMaximum(PaginationOptions? requested)
    {
        if (requested is not null && requested.PageSize > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested), requested.PageSize,
                $"The page size {requested.PageSize} exceeds the maximum of {MaxPageSize} configured by {nameof(PaginationLimits)}.");
        }
    }
}
