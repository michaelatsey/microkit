namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Generic EF Core read-side repository base implementing <see cref="IReadRepository{TAggregate}"/>.
/// Enforces <c>AsNoTracking()</c> on all queries; uses <see cref="ISpecificationEvaluator"/> for
/// all <see cref="QueryOptions{TAggregate}"/>-based transformations.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must be a reference type implementing <see cref="IAggregateRoot"/>.
/// </typeparam>
/// <typeparam name="TContext">
/// The application <see cref="DbContext"/> type.
/// </typeparam>
/// <remarks>
/// <para>
/// Extend this class for typed read repositories:
/// <code>
/// public sealed class EfUserReadRepository(AppDbContext ctx, ISpecificationEvaluator eval)
///     : EfReadRepository&lt;User, AppDbContext&gt;(ctx, eval), IUserReadRepository
/// {
///     public async ValueTask&lt;UserSummaryDto?&gt; GetSummaryAsync(UserId id, CancellationToken ct = default)
///         => await Context.Users
///                .AsNoTracking()
///                .Where(u => u.Id == id)
///                .Select(UserSummaryDto.Projection)
///                .FirstOrDefaultAsync(ct)
///                .ConfigureAwait(false);
/// }
/// </code>
/// </para>
/// <para>
/// <see cref="FindAsync"/> uses <c>DbSet.FindAsync</c> (cache-first) and then detaches the
/// returned entity to prevent accidental mutation tracking. Typed subclasses are encouraged
/// to override with a LINQ + <c>AsNoTracking()</c> query for truly read-only semantics.
/// </para>
/// </remarks>
public class EfReadRepository<TAggregate, TContext>(TContext context, ISpecificationEvaluator evaluator)
    : IReadRepository<TAggregate>
    where TAggregate : class, IAggregateRoot
    where TContext : DbContext
{
    /// <summary>
    /// The underlying <typeparamref name="TContext"/> DbContext.
    /// Available to typed subclasses for aggregate-specific query methods.
    /// </summary>
    protected TContext Context { get; } = context;

    /// <summary>
    /// Finds an aggregate by its primary key. The returned entity is detached from the change
    /// tracker to prevent accidental mutation tracking.
    /// </summary>
    /// <param name="keyValues">
    /// The primary key values. Pass a single-element array for simple PKs;
    /// multiple elements for composite PKs, in the same order as the key definition.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The matching aggregate, or <see langword="null"/> if not found.</returns>
    /// <remarks>
    /// Uses <c>DbSet&lt;T&gt;.FindAsync</c> which checks the change tracker before hitting the
    /// database. The entity is detached after retrieval. Override with a LINQ + <c>AsNoTracking()</c>
    /// query for fully read-only semantics when the change tracker should not be consulted.
    /// </remarks>
    public async ValueTask<TAggregate?> FindAsync(object[] keyValues, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        var entity = await Context.Set<TAggregate>().FindAsync(keyValues, ct).ConfigureAwait(false);
        if (entity is not null && Context.Entry(entity).State != EntityState.Detached)
            Context.Entry(entity).State = EntityState.Detached;
        return entity;
    }

    /// <summary>
    /// Returns all aggregates that satisfy the supplied <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">
    /// The query options specifying the filter, includes, ordering, and tracking hint.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// A read-only list of matching aggregates. Returns an empty list (never
    /// <see langword="null"/>) when no aggregates match.
    /// </returns>
    public async ValueTask<IReadOnlyList<TAggregate>> ListAsync(
        QueryOptions<TAggregate> opts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        var query = evaluator.GetQuery(GetBaseQuery(opts), opts);
        return await query.ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a paginated list of aggregates that satisfy the supplied
    /// <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">
    /// The query options. <see cref="QueryOptions{TAggregate}.Pagination"/> should be set;
    /// if omitted, all matching items are returned on a single page.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// An <see cref="IPagedResult{TAggregate}"/> containing the current page of results
    /// and the total count across all pages.
    /// </returns>
    /// <remarks>
    /// Executes two database queries: one <c>COUNT</c> (without pagination) and one <c>SELECT</c>
    /// (with <c>Skip/Take</c> applied). Ordering is applied to both.
    /// </remarks>
    public async ValueTask<IPagedResult<TAggregate>> ListPagedAsync(
        QueryOptions<TAggregate> opts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        var baseQuery = GetBaseQuery(opts);

        var totalCount = await evaluator
            .GetQuery(baseQuery, AsCountQuery(opts))
            .CountAsync(ct).ConfigureAwait(false);

        var items = await evaluator
            .GetQuery(baseQuery, opts)
            .ToListAsync(ct).ConfigureAwait(false);

        var pagination = opts.Pagination ?? new PaginationOptions(Page: 1, PageSize: totalCount > 0 ? totalCount : 1);
        return new PagedResult<TAggregate>(items, totalCount, pagination.Page, pagination.PageSize);
    }

    /// <summary>
    /// Returns <see langword="true"/> if at least one aggregate satisfies the supplied
    /// <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">The query options specifying the filter.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// <see langword="true"/> if at least one aggregate matches; <see langword="false"/> otherwise.
    /// </returns>
    /// <remarks>
    /// Prefer <see cref="AnyAsync"/> over <see cref="CountAsync"/> for existence checks —
    /// the database engine short-circuits after finding the first matching row.
    /// Includes, ordering, and pagination are stripped before execution.
    /// </remarks>
    public async ValueTask<bool> AnyAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        return await evaluator
            .GetQuery(GetBaseQuery(opts), AsCountQuery(opts))
            .AnyAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the total number of aggregates that satisfy the supplied
    /// <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">The query options specifying the filter.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The count of matching aggregates.</returns>
    /// <remarks>
    /// Includes, ordering, and pagination are stripped before execution.
    /// </remarks>
    public async ValueTask<int> CountAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        return await evaluator
            .GetQuery(GetBaseQuery(opts), AsCountQuery(opts))
            .CountAsync(ct).ConfigureAwait(false);
    }

    private IQueryable<TAggregate> GetBaseQuery(QueryOptions<TAggregate> opts)
        => opts.AsNoTrackingEnabled
            ? Context.Set<TAggregate>().AsNoTracking()
            : Context.Set<TAggregate>().AsQueryable();

    private static QueryOptions<TAggregate> AsCountQuery(QueryOptions<TAggregate> opts)
        => opts with { Includes = null, OrderBy = null, Pagination = null, AsSplitQueryEnabled = false };
}
