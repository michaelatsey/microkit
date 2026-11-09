namespace MicroKit.Persistence;

/// <summary>
/// Encapsulates the loading strategy for a read query over <typeparamref name="TAggregate"/>
/// aggregates. Combines the WHAT (a domain <see cref="Specification{T}"/>) with the HOW
/// (eager includes, split query, ordering, pagination, soft-deleted rows) — ADR-002.
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
/// There is no tracking option: the read side never tracks. An aggregate a command modifies is
/// loaded whole through <c>IRepository&lt;TAggregate, TId&gt;</c>, never through a read query.
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
    /// Pass <see langword="null"/> to return all aggregates (subject to the model's query filters).
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
    /// <see langword="null"/> means no pagination: <c>ListAsync</c> returns the full result set,
    /// <c>ListPagedAsync</c> returns the first page of <see cref="PaginationLimits.DefaultPageSize"/>
    /// items. A requested page size above <see cref="PaginationLimits.MaxPageSize"/> is rejected.
    /// </summary>
    public PaginationOptions? Pagination { get; init; }

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
    /// Must be set whenever <see cref="Pagination"/> is non-null, and for every
    /// <c>ListPagedAsync</c> call (which applies a default page when <see cref="Pagination"/> is
    /// null), to ensure deterministic page boundaries across requests.
    /// </remarks>
    public Func<IQueryable<TAggregate>, IOrderedQueryable<TAggregate>>? OrderBy { get; init; }

    /// <summary>
    /// Gets a value indicating whether soft-deleted aggregates are returned too.
    /// </summary>
    /// <remarks>
    /// The EF Core read side then ignores the soft-delete filter only (its named filter,
    /// <c>SoftDelete.FilterName</c>), for the whole query: an included navigation that reaches another
    /// soft-deletable aggregate returns its soft-deleted rows too. Every other query filter of the model,
    /// named or anonymous (a tenant filter), still applies. It has no effect on the in-memory doubles,
    /// which delete for real. The write side never loads a soft-deleted aggregate.
    /// </remarks>
    public bool IncludeDeleted { get; init; }
}
