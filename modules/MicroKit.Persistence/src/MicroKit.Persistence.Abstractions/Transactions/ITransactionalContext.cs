namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Provides ambient database transaction management for cross-aggregate operations
/// that require all-or-nothing semantics.
/// </summary>
/// <remarks>
/// <see cref="ITransactionalContext"/> is consumed by <c>TransactionBehavior</c> in
/// <c>MicroKit.MediatR.Behaviors</c>, which automatically wraps <c>ICommand</c> handlers
/// in a database transaction. It is also available for direct injection in command handlers
/// that require explicit multi-aggregate transaction control.
/// <para>
/// Implementations must clean up the underlying transaction on disposal via
/// <see cref="IAsyncDisposable.DisposeAsync"/>. Disposing an uncommitted context
/// performs a rollback.
/// </para>
/// </remarks>
public interface ITransactionalContext : IAsyncDisposable
{
    /// <summary>
    /// Begins an explicit database transaction.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// An <see cref="ITransaction"/> representing the active transaction.
    /// The caller is responsible for committing or rolling back.
    /// </returns>
    ValueTask<ITransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Commits the current explicit transaction, persisting all pending changes.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <exception cref="PersistenceException">
    /// Thrown when the underlying provider fails to commit.
    /// </exception>
    ValueTask CommitTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Rolls back the current explicit transaction, discarding all pending changes.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    ValueTask RollbackTransactionAsync(CancellationToken ct = default);
}
