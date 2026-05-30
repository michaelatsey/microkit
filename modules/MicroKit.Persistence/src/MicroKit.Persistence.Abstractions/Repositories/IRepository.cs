namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Write-side repository for <typeparamref name="TAggregate"/> aggregates.
/// Provides staging operations (Add, Update, Delete) and the Unit of Work commit boundary
/// via <see cref="CommitAsync"/>.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must implement <see cref="IAggregateRoot"/>.
/// </typeparam>
/// <remarks>
/// Staging methods (<see cref="AddAsync"/>, <see cref="UpdateAsync"/>, <see cref="DeleteAsync"/>)
/// do not persist changes immediately. Changes are committed atomically when
/// <see cref="CommitAsync"/> is called — exactly once per command handler invocation.
/// Inject <see cref="IRepository{TAggregate}"/> in command handlers only.
/// Typed custom repositories (e.g., <c>IUserRepository : IRepository&lt;User&gt;</c>)
/// declare strongly-typed <c>FindAsync(UserId id, ...)</c> methods.
/// </remarks>
public interface IRepository<TAggregate>
    where TAggregate : IAggregateRoot
{
    /// <summary>
    /// Stages a new aggregate for insertion into the underlying store.
    /// Changes are not persisted until <see cref="CommitAsync"/> is called.
    /// </summary>
    /// <param name="aggregate">The aggregate to insert. Must not already exist.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default);

    /// <summary>
    /// Stages an existing aggregate for update in the underlying store.
    /// Changes are not persisted until <see cref="CommitAsync"/> is called.
    /// </summary>
    /// <param name="aggregate">The aggregate to update. Must already exist.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    ValueTask UpdateAsync(TAggregate aggregate, CancellationToken ct = default);

    /// <summary>
    /// Stages an aggregate for deletion from the underlying store.
    /// Changes are not persisted until <see cref="CommitAsync"/> is called.
    /// </summary>
    /// <param name="aggregate">The aggregate to delete.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    ValueTask DeleteAsync(TAggregate aggregate, CancellationToken ct = default);

    /// <summary>
    /// Commits all pending changes accumulated since the last commit to the underlying store.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <exception cref="PersistenceException">
    /// Thrown when the underlying provider fails to commit (connection failure,
    /// constraint violation, or concurrency conflict).
    /// </exception>
    ValueTask CommitAsync(CancellationToken ct = default);
}
