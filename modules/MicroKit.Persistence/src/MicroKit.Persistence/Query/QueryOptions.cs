namespace MicroKit.Persistence;

/// <summary>
/// Encapsulates the loading strategy for a read query over <typeparamref name="TAggregate"/>
/// aggregates. Combines the WHAT (a domain <see cref="Specification{T}"/>) with the HOW
/// (eager includes, change-tracking hint, ordering, pagination, soft-delete visibility) — ADR-002.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must be a reference type implementing <see cref="IAggregateRoot"/>.
/// The <c>class</c> constraint is required because <see cref="Includes"/> and <see cref="OrderBy"/>
/// operate on <c>IQueryable&lt;TAggregate&gt;</c>, which mandates a reference type.
/// </typeparam>
/// <remarks>
/// <para>
/// All properties are <c>init</c>-only; use record <c>with</c> expressions or the fluent builder
/// extension methods in <see cref="QueryOptionsExtensions"/> to derive modified options.
/// </para>
/// <para>
/// <see cref="AsNoTrackingEnabled"/> defaults to <see langword="true"/> — read paths must not
/// track entities. Set <see langword="false"/> only when the aggregate will be mutated inside a
/// command handler after loading it via a read path.
/// </para>
/// <example>
/// <code>
/// var opts = new QueryOptions&lt;User&gt;(new ActiveUserSpec())
///     .WithIncludes(q => q.Include(u => u.Roles))
///     .WithPagination(page: 1, pageSize: 20)
///     .AsSplitQuery();
///
/// var page = await _repo.ListPagedAsync(opts, ct);
/// </code>
/// </example>
/// </remarks>
public sealed record QueryOptions<TAggregate>
    where TAggregate : class, IAggregateRoot
{
    /// <summary>
    /// Initializes a new <see cref="QueryOptions{TAggregate}"/> with an optional specification.
    /// </summary>
    /// <param name="specification">
    /// The domain specification that determines which aggregates match the query.
    /// Pass <see langword="null"/> to return all aggregates (subject to soft-delete filtering).
    /// </param>
    public QueryOptions(Specification<TAggregate>? specification = null)
    {
        Specification = specification;
    }

    /// <summary>
    /// Gets the domain specification defining the filter predicate (WHAT to query).
    /// <see langword="null"/> means no filter — all aggregates are candidates.
    /// </summary>
    public Specification<TAggregate>? Specification { get; init; }

    /// <summary>
    /// Gets the eager-loading delegate applied to the <c>IQueryable&lt;TAggregate&gt;</c>
    /// before query execution. Use to chain <c>Include()</c> / <c>ThenInclude()</c> calls.
    /// <see langword="null"/> means no eager loading.
    /// </summary>
    /// <remarks>
    /// For queries with two or more collection includes, combine with
    /// <see cref="AsSplitQueryEnabled"/> to avoid a Cartesian product row explosion.
    /// </remarks>
    public Func<IQueryable<TAggregate>, IQueryable<TAggregate>>? Includes { get; init; }

    /// <summary>
    /// Gets the pagination parameters.
    /// <see langword="null"/> means no pagination — the full result set is returned.
    /// </summary>
    public PaginationOptions? Pagination { get; init; }

    /// <summary>
    /// Gets a value indicating whether queries execute without EF Core change tracking.
    /// </summary>
    /// <value>
    /// <see langword="true"/> by default. Set to <see langword="false"/> only when the loaded
    /// aggregate will be mutated and committed via <c>IRepository&lt;TAggregate&gt;</c>.
    /// </value>
    public bool AsNoTrackingEnabled { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether EF Core split-query mode is active.
    /// When <see langword="true"/>, collection <c>Include()</c> navigations are fetched
    /// in separate SQL statements to avoid Cartesian product row explosion.
    /// </summary>
    /// <remarks>
    /// Recommended when <see cref="Includes"/> chains two or more collection navigations.
    /// </remarks>
    public bool AsSplitQueryEnabled { get; init; }

    /// <summary>
    /// Gets the ordering delegate applied after filtering and includes, and before pagination.
    /// <see langword="null"/> means no explicit ordering (database default order).
    /// </summary>
    /// <remarks>
    /// Must be set whenever <see cref="Pagination"/> is non-null to ensure deterministic
    /// page boundaries across requests.
    /// </remarks>
    public Func<IQueryable<TAggregate>, IOrderedQueryable<TAggregate>>? OrderBy { get; init; }

    /// <summary>
    /// Gets a value indicating whether soft-deleted aggregates are included in results.
    /// </summary>
    /// <value>
    /// <see langword="false"/> by default — soft-deleted rows are excluded.
    /// Set to <see langword="true"/> for administrative or audit queries.
    /// </value>
    /// <remarks>
    /// The <see cref="ISpecificationEvaluator"/> implementation is responsible for honouring
    /// this flag by bypassing the global query filter for soft-deleted entities.
    /// </remarks>
    public bool IncludeDeleted { get; init; }
}
