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
    /// returned; use <see cref="ListPagedAsync"/> for a bounded page.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="QueryOptions{TAggregate}.Pagination"/> asks for more than the configured
    /// <see cref="PaginationLimits.MaxPageSize"/> items.
    /// </exception>
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
    ValueTask<IPagedResult<TAggregate>> ListPagedAsync(
        QueryOptions<TAggregate> opts,
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
    ValueTask<bool> AnyAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);

    /// <summary>
    /// Returns the total number of aggregates that satisfy the supplied
    /// <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">The query options specifying the filter.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The count of matching aggregates.</returns>
    ValueTask<int> CountAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
}
