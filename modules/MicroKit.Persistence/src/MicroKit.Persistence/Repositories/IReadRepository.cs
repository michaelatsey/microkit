using System.Linq.Expressions;

namespace MicroKit.Persistence;

/// <summary>
/// Read-side repository contract for <typeparamref name="TAggregate"/> aggregates. Declared in
/// <c>MicroKit.Persistence</c> (Core), next to <see cref="QueryOptions{TAggregate}"/> on which its
/// query methods depend; it is the only read repository contract.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must be a reference type implementing <see cref="IAggregateRoot"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// Inject this interface (or a typed extension such as <c>IUserReadRepository</c>) in
/// query handlers only. Command handlers must never inject a read repository.
/// </para>
/// <para>
/// The read side never tracks: every method returns new, untracked instances, and never an
/// instance the current unit of work tracks. Only <c>IRepository&lt;TAggregate, TId&gt;</c> loads
/// aggregates for modification.
/// </para>
/// <para>
/// Declare typed read repositories as direct extensions:
/// <code>
/// public interface IUserReadRepository : IReadRepository&lt;User&gt;
/// {
///     ValueTask&lt;UserSummaryDto?&gt; GetSummaryAsync(UserId id, CancellationToken ct = default);
/// }
/// </code>
/// </para>
/// </remarks>
public interface IReadRepository<TAggregate>
    where TAggregate : class, IAggregateRoot
{
    /// <summary>
    /// Finds an aggregate by its primary key.
    /// </summary>
    /// <remarks>
    /// The aggregate is loaded without change tracking, as a new instance, even when the current
    /// unit of work already tracks it: a pending change of the command is not visible through it.
    /// A <see langword="null"/> key value matches nothing.
    /// </remarks>
    /// <param name="keyValues">
    /// The primary key values. Pass a single-element array for simple PKs;
    /// multiple elements for composite PKs, in the same order as the key definition.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The matching aggregate, or <see langword="null"/> if not found.</returns>
    ValueTask<TAggregate?> FindAsync(object[] keyValues, CancellationToken ct = default);

    /// <summary>
    /// Returns all aggregates that satisfy the supplied <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">
    /// The query options specifying the filter, includes and ordering.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// A read-only list of matching aggregates. Returns an empty list (never
    /// <see langword="null"/>) when no aggregates match.
    /// </returns>
    /// <remarks>
    /// Without <see cref="QueryOptions{TAggregate}.Pagination"/> every matching aggregate is
    /// returned; use <see cref="ListPagedAsync(QueryOptions{TAggregate}, CancellationToken)"/> for a bounded page.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="QueryOptions{TAggregate}.Pagination"/> asks for more than the configured
    /// <see cref="PaginationLimits.MaxPageSize"/> items.
    /// </exception>
    /// <exception cref="ArgumentException"><see cref="QueryOptions{TAggregate}.Keyset"/> is set: use <see cref="ListKeysetAsync(QueryOptions{TAggregate}, CancellationToken)"/>.</exception>
    ValueTask<IReadOnlyList<TAggregate>> ListAsync(
        QueryOptions<TAggregate> opts,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated list of aggregates that satisfy the supplied
    /// <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">
    /// The query options. When <see cref="QueryOptions{TAggregate}.Pagination"/> is not set, the
    /// first page of <see cref="PaginationLimits.DefaultPageSize"/> items is returned — never the
    /// whole result set.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// An <see cref="IPagedResult{TAggregate}"/> containing the current page of results
    /// and the total count across all pages.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="QueryOptions{TAggregate}.Pagination"/> asks for more than the configured
    /// <see cref="PaginationLimits.MaxPageSize"/> items.
    /// </exception>
    /// <exception cref="ArgumentException"><see cref="QueryOptions{TAggregate}.Keyset"/> is set: use <see cref="ListKeysetAsync(QueryOptions{TAggregate}, CancellationToken)"/>.</exception>
    ValueTask<IPagedResult<TAggregate>> ListPagedAsync(
        QueryOptions<TAggregate> opts,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the projection of every aggregate that satisfies the supplied
    /// <see cref="QueryOptions{TAggregate}"/>: the DTO a query handler returns, built by the database.
    /// </summary>
    /// <typeparam name="TResult">The projection type.</typeparam>
    /// <param name="opts">
    /// The query options: the specification, the ordering and the pagination apply, and the split hint splits the
    /// collections <paramref name="selector"/> projects; the includes do not (the projection reads only what
    /// <paramref name="selector"/> names).
    /// </param>
    /// <param name="selector">The projection, an expression the provider translates.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The projections, never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="QueryOptions{TAggregate}.Pagination"/> asks for more than <see cref="PaginationLimits.MaxPageSize"/> items.
    /// </exception>
    /// <exception cref="ArgumentException"><see cref="QueryOptions{TAggregate}.Keyset"/> is set: use <see cref="ListKeysetAsync{TResult}"/>.</exception>
    ValueTask<IReadOnlyList<TResult>> ListAsync<TResult>(
        QueryOptions<TAggregate> opts,
        Expression<Func<TAggregate, TResult>> selector,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a page of the projections of the aggregates that satisfy the supplied
    /// <see cref="QueryOptions{TAggregate}"/>, with the total count.
    /// </summary>
    /// <typeparam name="TResult">The projection type.</typeparam>
    /// <param name="opts">As for <see cref="ListPagedAsync(QueryOptions{TAggregate}, CancellationToken)"/>; the includes do not apply.</param>
    /// <param name="selector">The projection, an expression the provider translates.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The page of projections and the total count across all pages.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="QueryOptions{TAggregate}.Pagination"/> asks for more than <see cref="PaginationLimits.MaxPageSize"/> items.
    /// </exception>
    /// <exception cref="ArgumentException"><see cref="QueryOptions{TAggregate}.Keyset"/> is set: use <see cref="ListKeysetAsync{TResult}"/>.</exception>
    ValueTask<IPagedResult<TResult>> ListPagedAsync<TResult>(
        QueryOptions<TAggregate> opts,
        Expression<Func<TAggregate, TResult>> selector,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a keyset (cursor) page of the aggregates that satisfy the supplied
    /// <see cref="QueryOptions{TAggregate}"/>, ordered by <see cref="QueryOptions{TAggregate}.Keyset"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The page starts after <see cref="KeysetPagination{TAggregate}.After"/>, by comparing the sort keys in the
    /// database, and costs the same whatever its depth, unlike an offset. The root's primary key is appended,
    /// ascending, as the last sort key. Paging is forward only; the page's <see cref="KeysetPage{T}.Next"/>
    /// starts the next one. There is no total count.
    /// </para>
    /// <para>
    /// The cursor is checked here: one issued for another aggregate type or another ordering, or whose values do
    /// not match the sort keys, throws <see cref="ArgumentException"/>.
    /// </para>
    /// <para>
    /// The cursor's codec is internal to the module (ADR-013): only the module's implementations issue and read
    /// cursors. Another implementation forwards to one of them, or throws <see cref="NotSupportedException"/>.
    /// </para>
    /// </remarks>
    /// <param name="opts">The query options: the specification, the includes and the split hint apply; the keyset orders and pages.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The page, and the cursor of the next one.</returns>
    /// <exception cref="ArgumentException">
    /// No keyset is set; an ordering or an offset pagination is set too; a sort key is not a non-nullable scalar
    /// property of the root; or the cursor does not belong to this ordering.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A sort key is stored as a type a <see cref="KeysetCursor"/> cannot carry, reads null on a row, or the next
    /// cursor would exceed <see cref="KeysetCursor.MaxTokenLength"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The keyset's page size exceeds <see cref="PaginationLimits.MaxPageSize"/>.</exception>
    ValueTask<KeysetPage<TAggregate>> ListKeysetAsync(
        QueryOptions<TAggregate> opts,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a keyset (cursor) page of the projections of the aggregates that satisfy the supplied
    /// <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <remarks>As <see cref="ListKeysetAsync(QueryOptions{TAggregate}, CancellationToken)"/>; the cursor is read from the same rows as the projections.</remarks>
    /// <typeparam name="TResult">The projection type.</typeparam>
    /// <param name="opts">The query options: the specification and the split hint apply; the keyset orders and pages; the includes do not apply.</param>
    /// <param name="selector">The projection, an expression the provider translates.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The page of projections, and the cursor of the next one.</returns>
    /// <exception cref="ArgumentException">As for <see cref="ListKeysetAsync(QueryOptions{TAggregate}, CancellationToken)"/>.</exception>
    /// <exception cref="InvalidOperationException">As for <see cref="ListKeysetAsync(QueryOptions{TAggregate}, CancellationToken)"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The keyset's page size exceeds <see cref="PaginationLimits.MaxPageSize"/>.</exception>
    ValueTask<KeysetPage<TResult>> ListKeysetAsync<TResult>(
        QueryOptions<TAggregate> opts,
        Expression<Func<TAggregate, TResult>> selector,
        CancellationToken ct = default);

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
    /// </remarks>
    /// <exception cref="ArgumentException"><see cref="QueryOptions{TAggregate}.Keyset"/> is set.</exception>
    ValueTask<bool> AnyAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);

    /// <summary>
    /// Returns the total number of aggregates that satisfy the supplied
    /// <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">The query options specifying the filter.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The count of matching aggregates.</returns>
    /// <exception cref="ArgumentException"><see cref="QueryOptions{TAggregate}.Keyset"/> is set.</exception>
    ValueTask<int> CountAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
}
