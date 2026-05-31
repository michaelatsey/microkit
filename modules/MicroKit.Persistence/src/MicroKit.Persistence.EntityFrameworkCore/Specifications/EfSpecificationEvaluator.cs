namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="ISpecificationEvaluator"/>.
/// Applies a <see cref="QueryOptions{T}"/> to an <c>IQueryable&lt;T&gt;</c> in the canonical
/// 6-step evaluation order.
/// </summary>
/// <remarks>
/// <para>
/// The canonical evaluation order is:
/// <list type="number">
///   <item><description><b>IgnoreQueryFilters</b> — bypasses EF Core global query filters (e.g., soft-delete)
///     when <see cref="QueryOptions{T}.IncludeDeleted"/> is <see langword="true"/>. Must run
///     before <c>Where</c> to ensure predicates are not constrained by the filter.</description></item>
///   <item><description><b>Specification criteria</b> — applies <c>.Where(spec.Criteria)</c></description></item>
///   <item><description><b>Includes</b> — applies the <c>Includes</c> delegate
///     (e.g., <c>.Include(...).ThenInclude(...)</c>)</description></item>
///   <item><description><b>Split-query hint</b> — applies <c>.AsSplitQuery()</c> when
///     <see cref="QueryOptions{T}.AsSplitQueryEnabled"/> is <see langword="true"/></description></item>
///   <item><description><b>Ordering</b> — applies the <c>OrderBy</c> delegate</description></item>
///   <item><description><b>Pagination</b> — applies <c>.Skip(p.Skip).Take(p.PageSize)</c>
///     (must follow ordering for deterministic page boundaries)</description></item>
/// </list>
/// </para>
/// <para>
/// <b>AsNoTracking responsibility:</b> this evaluator does not apply <c>AsNoTracking()</c>.
/// The repository calling <see cref="GetQuery{T}"/> is responsible for passing a queryable
/// that already has the correct tracking behaviour applied. This keeps the evaluator
/// stateless and provider-agnostic.
/// </para>
/// </remarks>
public sealed class EfSpecificationEvaluator : ISpecificationEvaluator
{
    /// <summary>
    /// Transforms <paramref name="inputQuery"/> by applying all active properties of
    /// <paramref name="opts"/> in the canonical 6-step evaluation order.
    /// </summary>
    /// <typeparam name="T">The aggregate root type.</typeparam>
    /// <param name="inputQuery">
    /// The base <c>IQueryable&lt;T&gt;</c> to transform. The calling repository must have
    /// already applied <c>AsNoTracking()</c> when
    /// <see cref="QueryOptions{T}.AsNoTrackingEnabled"/> is <see langword="true"/>.
    /// </param>
    /// <param name="opts">The query options to apply.</param>
    /// <returns>
    /// A transformed <c>IQueryable&lt;T&gt;</c> with all active options applied.
    /// The query is not yet materialized — call <c>ToListAsync()</c>,
    /// <c>AnyAsync()</c>, <c>CountAsync()</c>, etc. on the returned queryable.
    /// </returns>
    public IQueryable<T> GetQuery<T>(IQueryable<T> inputQuery, QueryOptions<T> opts)
        where T : class, IAggregateRoot
    {
        var query = inputQuery;

        if (opts.IncludeDeleted)
            query = query.IgnoreQueryFilters();                        // step 0: bypass soft-delete filter

        if (opts.Specification is { } spec)
            query = query.Where(spec.ToExpression());                  // step 1: criteria

        if (opts.Includes is not null)
            query = opts.Includes(query);                              // step 2: eager loading

        if (opts.AsSplitQueryEnabled)
            query = query.AsSplitQuery();                              // step 3: split-query hint

        if (opts.OrderBy is not null)
            query = opts.OrderBy(query);                               // step 4: ordering

        if (opts.Pagination is { } p)
            query = query.Skip(p.Skip).Take(p.PageSize);              // step 5: pagination

        return query;
    }
}
