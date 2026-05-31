using Abs = MicroKit.Persistence.Abstractions;

namespace MicroKit.Persistence;

/// <summary>
/// Full read-side repository contract for <typeparamref name="TAggregate"/> aggregates.
/// Extends the empty marker in <c>MicroKit.Persistence.Abstractions</c> with the query
/// methods that depend on <see cref="QueryOptions{TAggregate}"/> (ADR-002, ADR-003).
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
/// All methods execute without EF Core change tracking by default when
/// <see cref="QueryOptions{TAggregate}.AsNoTrackingEnabled"/> is <see langword="true"/>
/// (the default).
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
    : Abs.IReadRepository<TAggregate>
    where TAggregate : class, IAggregateRoot
{
    /// <summary>
    /// Finds an aggregate by its primary key without change tracking.
    /// </summary>
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
    /// The query options specifying the filter, includes, ordering, and tracking hint.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// A read-only list of matching aggregates. Returns an empty list (never
    /// <see langword="null"/>) when no aggregates match.
    /// </returns>
    ValueTask<IReadOnlyList<TAggregate>> ListAsync(
        QueryOptions<TAggregate> opts,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated list of aggregates that satisfy the supplied
    /// <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">
    /// The query options. <see cref="QueryOptions{TAggregate}.Pagination"/> must be set.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// An <see cref="IPagedResult{TAggregate}"/> containing the current page of results
    /// and the total count across all pages.
    /// </returns>
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
