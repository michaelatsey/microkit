namespace MicroKit.Persistence;

using MicroKit.Result;

/// <summary>
/// The <see cref="Result"/> path of the unit of work: it returns a persistence conflict as a
/// <see cref="ErrorCategory.Conflict"/> error (<see cref="PersistenceErrors"/>) instead of throwing a
/// <see cref="PersistenceException"/>.
/// </summary>
/// <remarks>
/// <para>
/// It converts only at the boundary that owns the transaction, once the transaction is rolled back: a
/// failure converted inside the transaction would let the operation return normally, and the owner would
/// commit whatever the transaction wrote before it. Up to three failures are converted — a
/// unique-constraint violation (<c>DUPLICATE</c>), a foreign-key violation (<c>REFERENCE</c>) and an
/// optimistic concurrency conflict (<c>CONCURRENCY</c>); every other failure propagates as an exception,
/// as with <see cref="IUnitOfWork"/>. An implementation may recognise fewer: the EF Core unit of work
/// recognises unique and foreign-key violations only with the provider's classifier, and the in-memory
/// double recognises its version conflicts only.
/// </para>
/// <para>
/// It writes nothing by itself: pending changes are written by <see cref="IUnitOfWork.CommitAsync"/>
/// called inside the operation, or by <see cref="TryCommitAsync"/>. A handler therefore injects both
/// contracts — a <see cref="TryExecuteAsync{TState}"/> whose operation never calls
/// <see cref="IUnitOfWork.CommitAsync"/> succeeds and writes nothing.
/// </para>
/// <para>
/// It is additive: <see cref="IUnitOfWork.CommitAsync"/> and <see cref="ITransactionalContext"/> keep
/// their behaviour. A handler depends on this contract without referencing EF Core; the EF Core unit of
/// work and the in-memory double of <c>MicroKit.Persistence.Testing</c> implement it.
/// </para>
/// </remarks>
public interface IResultUnitOfWork
{
    /// <summary>
    /// Flushes the pending changes, as <see cref="IUnitOfWork.CommitAsync"/> does, and returns a
    /// persistence conflict as a failed <see cref="Result"/>. For code running with no open
    /// transaction, where the flush is atomic by itself.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// A success, or a failure carrying the <see cref="PersistenceErrors"/> error of the conflict; nothing
    /// is written on failure.
    /// </returns>
    /// <remarks>
    /// After a converted failure the change set is still pending, as after a failed
    /// <see cref="IUnitOfWork.CommitAsync"/>: committing again fails again. Call
    /// <see cref="IUnitOfWork.DiscardChanges"/>, or end the scope. <see cref="TryExecuteAsync{TState}"/>,
    /// by contrast, abandons the change set with the rolled-back transaction.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A transaction is open: a flush inside it is not atomic, so converting its failure could let the
    /// transaction's owner commit what was written before. Use
    /// <see cref="TryExecuteAsync{TState}"/> on the owner instead.
    /// </exception>
    /// <exception cref="PersistenceException">Any other persistence failure.</exception>
    ValueTask<Result> TryCommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="operation"/> as <see cref="ITransactionalContext"/> does — inside a transaction
    /// and the provider's execution strategy, transient failures retried — and returns a persistence
    /// conflict as a failed <see cref="Result"/> once that transaction is rolled back.
    /// </summary>
    /// <typeparam name="TState">Caller-owned state passed to <paramref name="operation"/> on each attempt.</typeparam>
    /// <param name="operation">The work to execute inside the transaction.</param>
    /// <param name="state">State passed to <paramref name="operation"/>.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// What <paramref name="operation"/> returned, or a failure carrying the <see cref="PersistenceErrors"/>
    /// error of the conflict.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Owner.</b> When no transaction is open, this call owns the one it begins. The operation writes
    /// its changes with <see cref="IUnitOfWork.CommitAsync"/>; a successful <see cref="Result"/> then
    /// commits the transaction. A failed <see cref="Result"/> that the operation returns itself is returned
    /// unchanged, and the transaction is rolled back instead of committed: a failed command never persists
    /// part of its work. A conflict is converted only after the rollback, and after the execution strategy
    /// has given up.
    /// </para>
    /// <para>
    /// <b>Retries.</b> A transient failure runs the whole operation again, with what it tracked
    /// abandoned: it must load what it modifies itself. When the failure hits the transaction's COMMIT,
    /// the database may already have committed, so an operation that is not idempotent can apply twice
    /// (at-least-once, as <see cref="ITransactionalContext"/>).
    /// </para>
    /// <para>
    /// <b>Joined.</b> When a transaction is already open, the operation runs inside it and nothing is
    /// converted: an exception propagates unchanged and a failed <see cref="Result"/> is returned
    /// unchanged; the transaction's owner decides its fate.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="PersistenceException">
    /// Any persistence failure that is not a recognised conflict; joined to an open transaction, every
    /// persistence failure, conflicts included, unconverted.
    /// </exception>
    ValueTask<Result> TryExecuteAsync<TState>(
        Func<TState, CancellationToken, Task<Result>> operation,
        TState state,
        CancellationToken ct = default);

    /// <inheritdoc cref="TryExecuteAsync{TState}(Func{TState, CancellationToken, Task{Result}}, TState, CancellationToken)"/>
    /// <typeparam name="TState">Caller-owned state passed to <paramref name="operation"/> on each attempt.</typeparam>
    /// <typeparam name="TValue">The value a successful <paramref name="operation"/> produces.</typeparam>
    ValueTask<Result<TValue>> TryExecuteAsync<TState, TValue>(
        Func<TState, CancellationToken, Task<Result<TValue>>> operation,
        TState state,
        CancellationToken ct = default);
}
