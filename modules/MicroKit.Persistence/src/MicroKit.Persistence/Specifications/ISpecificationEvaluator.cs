namespace MicroKit.Persistence;

/// <summary>
/// Applies a <see cref="QueryOptions{T}"/> to an <c>IQueryable&lt;T&gt;</c>, producing the
/// final query ready for materialization.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ISpecificationEvaluator"/> is declared in <c>MicroKit.Persistence</c> (Core)
/// and implemented by <c>EfSpecificationEvaluator</c> in
/// <c>MicroKit.Persistence.EntityFrameworkCore</c>.
/// </para>
/// <para>
/// The canonical evaluation order applied by all conforming implementations is:
/// <list type="number">
///   <item><description><b>Specification criteria</b> — applies <c>.Where(spec.ToExpression())</c></description></item>
///   <item><description><b>Includes</b> — applies the <c>Includes</c> delegate (e.g., <c>.Include(...).ThenInclude(...)</c>)</description></item>
///   <item><description><b>Split-query hint</b> — applies <c>.AsSplitQuery()</c> when <see cref="QueryOptions{T}.AsSplitQueryEnabled"/> is <see langword="true"/></description></item>
///   <item><description><b>Ordering</b> — applies the <c>OrderBy</c> delegate</description></item>
///   <item><description><b>Pagination</b> — applies <c>.Skip(...).Take(...)</c> (must follow ordering for deterministic results)</description></item>
/// </list>
/// The order is fixed to prevent invalid EF Core query states (e.g., <c>Skip</c> before <c>OrderBy</c>).
/// </para>
/// <para>
/// <b>AsNoTracking responsibility:</b> the evaluator does not apply <c>AsNoTracking()</c>.
/// The repository calling <see cref="GetQuery{T}"/> is responsible for passing a queryable
/// that already has the correct tracking behaviour applied. This separation keeps the
/// evaluator stateless and provider-agnostic.
/// </para>
/// </remarks>
public interface ISpecificationEvaluator
{
    /// <summary>
    /// Transforms <paramref name="inputQuery"/> by applying all active properties of
    /// <paramref name="opts"/> in the canonical evaluation order.
    /// </summary>
    /// <typeparam name="T">The aggregate root type.</typeparam>
    /// <param name="inputQuery">
    /// The base <c>IQueryable&lt;T&gt;</c> to transform. The caller (repository) must
    /// have already applied <c>AsNoTracking()</c> when
    /// <see cref="QueryOptions{T}.AsNoTrackingEnabled"/> is <see langword="true"/>.
    /// </param>
    /// <param name="opts">The query options to apply.</param>
    /// <returns>
    /// A transformed <c>IQueryable&lt;T&gt;</c> with all options applied.
    /// The query is not yet materialized — call <c>ToListAsync()</c>,
    /// <c>FirstOrDefaultAsync()</c>, etc. on the returned queryable.
    /// </returns>
    IQueryable<T> GetQuery<T>(IQueryable<T> inputQuery, QueryOptions<T> opts)
        where T : class, IAggregateRoot;
}
