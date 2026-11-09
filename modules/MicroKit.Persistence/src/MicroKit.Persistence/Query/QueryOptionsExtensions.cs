namespace MicroKit.Persistence;

/// <summary>
/// Fluent builder extension methods for constructing <see cref="QueryOptions{TAggregate}"/>
/// instances. Each method derives a new instance via record <c>with</c> expression, leaving
/// the original unchanged.
/// </summary>
public static class QueryOptionsExtensions
{
    /// <summary>
    /// Returns a new <see cref="QueryOptions{TAggregate}"/> with the specified eager-loading delegate.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root type.</typeparam>
    /// <param name="opts">The source options to derive from.</param>
    /// <param name="includes">
    /// A delegate that receives the base <c>IQueryable&lt;TAggregate&gt;</c> and returns it
    /// after applying <c>Include()</c> / <c>ThenInclude()</c> calls.
    /// </param>
    /// <returns>A new options instance with <see cref="QueryOptions{TAggregate}.Includes"/> set.</returns>
    public static QueryOptions<TAggregate> WithIncludes<TAggregate>(
        this QueryOptions<TAggregate> opts,
        Func<IQueryable<TAggregate>, IQueryable<TAggregate>> includes)
        where TAggregate : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(includes);
        return opts with { Includes = includes };
    }

    /// <summary>
    /// Returns a new <see cref="QueryOptions{TAggregate}"/> with pagination applied.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root type.</typeparam>
    /// <param name="opts">The source options to derive from.</param>
    /// <param name="page">The one-based page number. Must be ≥ 1.</param>
    /// <param name="pageSize">The number of items per page. Must be ≥ 1.</param>
    /// <returns>A new options instance with <see cref="QueryOptions{TAggregate}.Pagination"/> set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="opts"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="page"/> or <paramref name="pageSize"/> is less than 1, or the page starts
    /// beyond <see cref="int.MaxValue"/> items (see <see cref="PaginationOptions"/>).
    /// </exception>
    public static QueryOptions<TAggregate> WithPagination<TAggregate>(
        this QueryOptions<TAggregate> opts,
        int page,
        int pageSize)
        where TAggregate : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(opts);
        return opts with { Pagination = new PaginationOptions(page, pageSize) };
    }

    /// <summary>
    /// Returns a new <see cref="QueryOptions{TAggregate}"/> with the supplied
    /// <see cref="PaginationOptions"/> instance applied.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root type.</typeparam>
    /// <param name="opts">The source options to derive from.</param>
    /// <param name="pagination">The pagination parameters to apply.</param>
    /// <returns>A new options instance with <see cref="QueryOptions{TAggregate}.Pagination"/> set.</returns>
    public static QueryOptions<TAggregate> WithPagination<TAggregate>(
        this QueryOptions<TAggregate> opts,
        PaginationOptions pagination)
        where TAggregate : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(pagination);
        return opts with { Pagination = pagination };
    }

    /// <summary>
    /// Returns a new <see cref="QueryOptions{TAggregate}"/> with EF Core split-query mode
    /// enabled. Recommended when <see cref="QueryOptions{TAggregate}.Includes"/> chains two
    /// or more collection navigation properties to prevent Cartesian product row explosion.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root type.</typeparam>
    /// <param name="opts">The source options to derive from.</param>
    /// <returns>A new options instance with <see cref="QueryOptions{TAggregate}.AsSplitQueryEnabled"/> set to <see langword="true"/>.</returns>
    public static QueryOptions<TAggregate> AsSplitQuery<TAggregate>(
        this QueryOptions<TAggregate> opts)
        where TAggregate : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(opts);
        return opts with { AsSplitQueryEnabled = true };
    }

    /// <summary>
    /// Returns a new <see cref="QueryOptions{TAggregate}"/> that also returns soft-deleted aggregates
    /// (<see cref="QueryOptions{TAggregate}.IncludeDeleted"/>): only the soft-delete filter is ignored,
    /// every other query filter still applies.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root type.</typeparam>
    /// <param name="opts">The source options to derive from.</param>
    /// <returns>A new options instance with <see cref="QueryOptions{TAggregate}.IncludeDeleted"/> set to <see langword="true"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="opts"/> is <see langword="null"/>.</exception>
    public static QueryOptions<TAggregate> WithDeleted<TAggregate>(
        this QueryOptions<TAggregate> opts)
        where TAggregate : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(opts);
        return opts with { IncludeDeleted = true };
    }

    /// <summary>
    /// Returns a new <see cref="QueryOptions{TAggregate}"/> with the specified ordering delegate,
    /// replacing any previous ordering. Ordering is applied after filtering and includes, and before
    /// pagination.
    /// </summary>
    /// <remarks>
    /// Named with the <c>With</c> prefix, like <c>WithOrderBy</c> and <c>WithThenBy</c>: an extension
    /// named <c>OrderBy</c> loses to the <see cref="QueryOptions{TAggregate}.OrderBy"/> property and
    /// cannot be called with extension syntax. For a key selector, the
    /// <c>MicroKit.Persistence.Specifications</c> package offers <c>WithOrderBy</c> and <c>WithThenBy</c>.
    /// </remarks>
    /// <typeparam name="TAggregate">The aggregate root type.</typeparam>
    /// <param name="opts">The source options to derive from.</param>
    /// <param name="orderBy">
    /// A delegate that receives the <c>IQueryable&lt;TAggregate&gt;</c> and returns an
    /// <c>IOrderedQueryable&lt;TAggregate&gt;</c>.
    /// Example: <c>q =&gt; q.OrderByDescending(u =&gt; u.CreatedAt)</c>.
    /// </param>
    /// <returns>A new options instance with <see cref="QueryOptions{TAggregate}.OrderBy"/> set.</returns>
    public static QueryOptions<TAggregate> WithOrdering<TAggregate>(
        this QueryOptions<TAggregate> opts,
        Func<IQueryable<TAggregate>, IOrderedQueryable<TAggregate>> orderBy)
        where TAggregate : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(orderBy);
        return opts with { OrderBy = orderBy };
    }
}
