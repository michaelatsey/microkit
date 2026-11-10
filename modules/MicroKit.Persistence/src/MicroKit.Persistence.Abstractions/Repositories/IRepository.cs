namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Write-side repository for <typeparamref name="TAggregate"/> aggregates, identified by
/// <typeparamref name="TId"/>: loads a whole aggregate for modification and stages additions and
/// deletions. It never commits — committing belongs to <see cref="IUnitOfWork"/> only.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must be a reference type implementing <see cref="IAggregateRoot"/>.
/// No base class is required: an implementation identifies the aggregate through its identifier.
/// </typeparam>
/// <typeparam name="TId">The aggregate's identifier type.</typeparam>
/// <remarks>
/// <para>
/// Inject it in command handlers. A loaded aggregate is tracked: mutate it through its own
/// methods and let the command's <see cref="IUnitOfWork.CommitAsync"/> — called by
/// <c>TransactionBehavior</c> in <c>MicroKit.MediatR.Behaviors</c> — write the changes. There is no
/// update method: a tracked aggregate needs none.
/// </para>
/// <para>
/// Declare a typed repository as a direct extension:
/// <code>
/// public interface IOrderRepository : IRepository&lt;Order, OrderId&gt;
/// {
///     ValueTask&lt;Order?&gt; FindByNumberAsync(OrderNumber number, CancellationToken ct = default);
/// }
/// </code>
/// </para>
/// </remarks>
public interface IRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot
    where TId : IEntityId
{
    /// <summary>
    /// Loads the whole aggregate identified by <paramref name="id"/> — its root and the entities
    /// it owns — tracked, so that changes made to it are written by the next commit.
    /// </summary>
    /// <param name="id">The aggregate's identifier.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The aggregate, or <see langword="null"/> if none has this identifier.</returns>
    ValueTask<TAggregate?> FindAsync(TId id, CancellationToken ct = default);

    /// <summary>
    /// Loads the whole aggregate identified by <paramref name="id"/>, as
    /// <see cref="FindAsync(TId, CancellationToken)"/> does, for a command that states the version it
    /// expects — the <c>If-Match</c> of an HTTP request, read earlier through
    /// <see cref="GetVersion"/>.
    /// </summary>
    /// <param name="id">The aggregate's identifier.</param>
    /// <param name="expectedVersion">The version the command was decided on.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The aggregate, or <see langword="null"/> if none has this identifier.</returns>
    /// <remarks>
    /// The expectation is checked when the change is written, not when the aggregate is loaded: an
    /// aggregate whose version has moved on makes the commit fail with a concurrency conflict
    /// (<see cref="PersistenceException"/>), and the conflicting write is not applied. An
    /// implementation that cannot defer the check may run it at the load and say so.
    /// </remarks>
    /// <exception cref="PersistenceException">
    /// An implementation that checks at the load found another version.
    /// </exception>
    ValueTask<TAggregate?> FindAsync(TId id, long expectedVersion, CancellationToken ct = default);

    /// <summary>
    /// Gets the version of an aggregate this repository loaded or staged — a counter that each
    /// committed save writing any entity of the aggregate increases by one — for an HTTP <c>ETag</c>
    /// or a later <see cref="FindAsync(TId, long, CancellationToken)"/>.
    /// </summary>
    /// <param name="aggregate">The aggregate.</param>
    /// <returns>
    /// Its version. For an aggregate staged but not yet committed, an implementation reports a
    /// provisional value (EF Core: 0 until the commit writes 1).
    /// </returns>
    long GetVersion(TAggregate aggregate);

    /// <summary>
    /// Stages a new aggregate for insertion. Nothing is written until the unit of work commits.
    /// </summary>
    /// <param name="aggregate">The aggregate to insert. Must not already exist.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default);

    /// <summary>
    /// Stages an aggregate for deletion. Nothing is written until the unit of work commits.
    /// </summary>
    /// <param name="aggregate">The aggregate to delete.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    ValueTask DeleteAsync(TAggregate aggregate, CancellationToken ct = default);
}
