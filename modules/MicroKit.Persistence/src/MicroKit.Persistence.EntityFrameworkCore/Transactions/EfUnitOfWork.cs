using System.Data.Common;
using System.Runtime.ExceptionServices;

namespace MicroKit.Persistence.EntityFrameworkCore;

using MicroKit.Result;

/// <summary>
/// EF Core implementation of <see cref="ITransactionalUnitOfWork"/>.
/// Wraps a <typeparamref name="TContext"/> <see cref="DbContext"/> and translates provider
/// exceptions into <see cref="PersistenceException"/>.
/// </summary>
/// <typeparam name="TContext">
/// The application <see cref="DbContext"/> type. Injected as a scoped service.
/// </typeparam>
/// <remarks>
/// <para>
/// Register via
/// <see cref="PersistenceServiceCollectionExtensions.AddUnitOfWork{TContext}"/>, which creates
/// a single scoped instance and binds it to <see cref="IUnitOfWork"/>,
/// <see cref="ITransactionalContext"/>, <see cref="ITransactionalUnitOfWork"/>, and
/// <see cref="IResultUnitOfWork"/>.
/// </para>
/// <para>
/// The <see cref="IResultUnitOfWork"/> path converts a concurrency conflict always, and a unique or
/// foreign-key violation when the context has the provider's <see cref="IDatabaseFailureClassifier"/>
/// (<c>UsePostgreSQL</c>, <c>UseSqlServer</c>, or <c>UseMicroKitPersistence(classifier)</c>). The code's
/// root comes from the model: the failing entity's aggregate root, named by
/// <see cref="AggregateErrorNameBuilderExtensions.HasAggregateErrorName(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder, string)"/>
/// or by default.
/// </para>
/// </remarks>
public sealed class EfUnitOfWork<TContext>(TContext context)
    : ITransactionalUnitOfWork, IResultUnitOfWork
    where TContext : DbContext
{
    // The PersistenceException CommitAsync raised and ExecuteAsync unwrapped so the execution
    // strategy could classify its DbUpdateException. Restored once the strategy gives up. The unit
    // of work is scoped and a DbContext is never used concurrently, so one slot suffices.
    private PersistenceException? _unwrapped;

    /// <summary>
    /// Flushes all pending EF Core change-tracker entries to the database via
    /// <c>SaveChangesAsync</c>. Translates provider failures into
    /// <see cref="PersistenceException"/>.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <exception cref="PersistenceException">
    /// Thrown on an optimistic concurrency conflict (<see cref="DbUpdateConcurrencyException"/>),
    /// on any other database write failure (<see cref="DbUpdateException"/>), and on a write
    /// failure the provider's non-retrying execution strategy classified as transient (EF Core
    /// surfaces that one as an <see cref="InvalidOperationException"/> wrapping the
    /// <see cref="DbUpdateException"/>), and, outside <see cref="ExecuteAsync{TState}"/>, when the
    /// provider's retrying execution strategy has exhausted its retries
    /// (<see cref="RetryLimitExceededException"/>). The original exception is preserved as
    /// <see cref="Exception.InnerException"/>.
    /// </exception>
    public async ValueTask CommitAsync(CancellationToken ct = default)
    {
        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new PersistenceException("Optimistic concurrency conflict during commit.", ex);
        }
        catch (DbUpdateException ex)
        {
            throw new PersistenceException("Database error during commit.", ex);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is DbUpdateException)
        {
            throw new PersistenceException(
                "Database error during commit. The provider classified it as transient; enable retry on failure to retry it.",
                ex);
        }
        catch (RetryLimitExceededException ex)
        {
            throw new PersistenceException("Database error during commit: the retries on failure are exhausted.", ex);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Clears the EF Core change tracker: every <c>Added</c>, <c>Modified</c>, and <c>Deleted</c>
    /// entry is detached and nothing is written. Purely in-memory — no I/O, no provider exception,
    /// nothing to translate into <see cref="PersistenceException"/>. It does not touch the ambient
    /// database transaction: the transaction's own commit or rollback still decides the fate of
    /// anything already flushed inside it, and rows committed before it began are unaffected.
    /// The record of the roots the repositories loaded whole is reset with it: an instance that was
    /// loaded before and is attached again afterwards is no longer known to be whole.
    /// </remarks>
    public void DiscardChanges() => ClearTracker();

    /// <summary>
    /// Executes <paramref name="operation"/> inside a database transaction. When no transaction
    /// is open, begins one inside the provider's execution strategy, commits on success and rolls
    /// back on failure; transient failures are retried when the provider's strategy retries. When
    /// a transaction is already open on the context, joins it.
    /// </summary>
    /// <typeparam name="TState">
    /// Caller-owned state threaded through to <paramref name="operation"/>.
    /// Avoids lambda closures on the hot path.
    /// </typeparam>
    /// <param name="operation">The work to execute inside the transaction.</param>
    /// <param name="state">State passed to <paramref name="operation"/> on each attempt.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <remarks>
    /// <para>
    /// <b>Join.</b> While <c>Database.CurrentTransaction</c> is not <see langword="null"/> — an
    /// enclosing <see cref="ExecuteAsync{TState}"/> or a transaction the caller opened — the
    /// operation runs directly inside it: no execution strategy, no begin, commit or rollback.
    /// Retry and translation belong to the outermost call; the transaction's owner decides its fate.
    /// </para>
    /// <para>
    /// <b>Retry.</b> A <see cref="PersistenceException"/> that <see cref="CommitAsync"/> raised from a
    /// <see cref="DbUpdateException"/> is unwrapped inside the strategy, so the strategy can
    /// classify the failure (it only looks through exceptions EF Core created) and re-run the
    /// operation when it is transient. Once the strategy gives up, the caller observes a
    /// <see cref="PersistenceException"/> only: the original one for a non-transient failure, a
    /// new one wrapping <see cref="RetryLimitExceededException"/> when the retries are exhausted.
    /// </para>
    /// <para>
    /// <b>Failed attempt.</b> The rollback runs with <see cref="CancellationToken.None"/> and never
    /// replaces the exception in flight: the caller observes the operation's own failure even when
    /// <paramref name="ct"/> was cancelled or the rollback itself failed. The change tracker is then
    /// cleared, because the database was rolled back: a retried attempt starts from a clean tracker,
    /// and after a final failure no entity of the attempt stays tracked, nor recorded as loaded whole.
    /// </para>
    /// <para>
    /// Because the tracker is cleared, a retried operation must load what it modifies itself:
    /// an entity tracked before <c>ExecuteAsync</c> (or passed in <paramref name="state"/>) is
    /// detached by a failed attempt, and changing it in the next attempt writes nothing. Resolve an
    /// optimistic concurrency conflict inside the operation, while the failed entries are still
    /// tracked; once the attempt is abandoned they are detached.
    /// </para>
    /// <para>
    /// <b>At-least-once.</b> With a retrying strategy, the whole operation runs again after a
    /// transient failure. When the failure hits the transaction's COMMIT, the database may already
    /// have committed: no verification runs (<c>verifySucceeded</c> is not used), so an operation
    /// that is not idempotent — store-generated keys, external side effects — can apply twice.
    /// Keep retries off for such operations, or make them idempotent.
    /// </para>
    /// <para>
    /// An ambient <c>System.Transactions</c> transaction is rejected, as EF Core rejects it: with a
    /// retrying strategy, the strategy refuses to start (<c>ExecutionStrategyExistingTransaction</c>);
    /// without one, beginning the transaction fails (<c>ConflictingAmbientTransaction</c>). Both surface as
    /// an <see cref="InvalidOperationException"/>. Only <c>Database.CurrentTransaction</c> is joined.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="PersistenceException">
    /// The commit, or the transaction itself (begin, commit, a deferred constraint), failed and was
    /// not, or could no longer be, retried. A write the operation flushed through the
    /// <see cref="DbContext"/> itself, rather than <see cref="CommitAsync"/>, is translated the same way. In the join path, the operation's exceptions propagate
    /// unchanged: the transaction's owner handles them.
    /// </exception>
    public Task ExecuteAsync<TState>(
        Func<TState, CancellationToken, Task> operation,
        TState state,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteCoreAsync(
            static async (s, token) =>
            {
                await s.Operation(s.State, token).ConfigureAwait(false);
                return true;
            },
            (Operation: operation, State: state),
            ct);
    }

    /// <inheritdoc cref="ExecuteAsync{TState}(Func{TState, CancellationToken, Task}, TState, CancellationToken)"/>
    /// <typeparam name="TState">
    /// Caller-owned state threaded through to <paramref name="operation"/>.
    /// Avoids lambda closures on the hot path.
    /// </typeparam>
    /// <typeparam name="TResult">The type of the value returned by <paramref name="operation"/>.</typeparam>
    /// <returns>The value produced by <paramref name="operation"/>.</returns>
    public Task<TResult> ExecuteAsync<TState, TResult>(
        Func<TState, CancellationToken, Task<TResult>> operation,
        TState state,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteCoreAsync(operation, state, ct);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It flushes through <see cref="CommitAsync"/> and converts its <see cref="PersistenceException"/>. The
    /// change tracker is not cleared after a converted failure: the failed <c>Added</c>, <c>Modified</c> and
    /// <c>Deleted</c> entries stay pending, so call <see cref="DiscardChanges"/> before reusing the scope.
    /// Unique and foreign-key violations are converted only when the context has the provider's
    /// <see cref="IDatabaseFailureClassifier"/>.
    /// </remarks>
    public async ValueTask<Result> TryCommitAsync(CancellationToken ct = default)
    {
        EnsureNoTransactionForFlush();
        try
        {
            await CommitAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (PersistenceException ex) when (PersistenceFailureMapper.TryMap(ex, context, out var error))
        {
            return Result.Failure(error);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Owning the transaction, it runs the operation exactly as
    /// <see cref="ExecuteAsync{TState}"/> does — execution strategy, one transaction per attempt,
    /// transient failures retried, the tracker cleared after a failed attempt — and converts a conflict
    /// once the strategy has given up. A failed <see cref="Result"/> the operation returns rolls the
    /// transaction back, and the tracker is cleared, as after any failed attempt; it is not retried. Joined
    /// to an open transaction, it runs the operation inside it and converts nothing.
    /// </para>
    /// <para>
    /// A strategy that retries every exception, rather than the provider's transient ones, would also
    /// re-run an operation that returned a failed <see cref="Result"/>.
    /// </para>
    /// </remarks>
    public async ValueTask<Result> TryExecuteAsync<TState>(
        Func<TState, CancellationToken, Task<Result>> operation,
        TState state,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (context.Database.CurrentTransaction is not null)
            return await operation(state, ct).ConfigureAwait(false);

        try
        {
            return await ExecuteCoreAsync(
                static async (s, token) =>
                {
                    var result = await s.Operation(s.State, token).ConfigureAwait(false);
                    return result.IsFailure ? throw new FailedResultRollback<Result>(result) : result;
                },
                (Operation: operation, State: state),
                ct).ConfigureAwait(false);
        }
        catch (FailedResultRollback<Result> rollback)
        {
            return rollback.Result;
        }
        catch (PersistenceException ex) when (PersistenceFailureMapper.TryMap(ex, context, out var error))
        {
            return Result.Failure(error);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// As <see cref="TryExecuteAsync{TState}(Func{TState, CancellationToken, Task{Result}}, TState, CancellationToken)"/>:
    /// the same strategy, transaction, rollback of a failed <see cref="Result{TValue}"/> and conversion.
    /// </remarks>
    public async ValueTask<Result<TValue>> TryExecuteAsync<TState, TValue>(
        Func<TState, CancellationToken, Task<Result<TValue>>> operation,
        TState state,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (context.Database.CurrentTransaction is not null)
            return await operation(state, ct).ConfigureAwait(false);

        try
        {
            return await ExecuteCoreAsync(
                static async (s, token) =>
                {
                    var result = await s.Operation(s.State, token).ConfigureAwait(false);
                    return result.IsFailure ? throw new FailedResultRollback<Result<TValue>>(result) : result;
                },
                (Operation: operation, State: state),
                ct).ConfigureAwait(false);
        }
        catch (FailedResultRollback<Result<TValue>> rollback)
        {
            return rollback.Result;
        }
        catch (PersistenceException ex) when (PersistenceFailureMapper.TryMap(ex, context, out var error))
        {
            return Result<TValue>.Failure(error);
        }
    }

    private void EnsureNoTransactionForFlush()
    {
        if (context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "TryCommitAsync is refused inside an open transaction: a flush there is not atomic, and converting its "
                + "failure would let the transaction's owner commit what was written before it. Commit with "
                + "CommitAsync, and run the command through TryExecuteAsync on the transaction's owner.");
        }
    }

    private async Task<TResult> ExecuteCoreAsync<TState, TResult>(
        Func<TState, CancellationToken, Task<TResult>> operation,
        TState state,
        CancellationToken ct)
    {
        // Join: a transaction is already open on this context — ours (a nested call) or the
        // caller's. Run the operation inside it as is: no execution strategy (a retrying one
        // rejects an existing transaction, and retrying belongs to whoever owns the transaction),
        // no begin, commit or rollback. The owner of the transaction decides its fate.
        if (context.Database.CurrentTransaction is not null)
            return await operation(state, ct).ConfigureAwait(false);

        var strategy = context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(
                (UnitOfWork: this, Operation: operation, State: state),
                static (_, s, token) => s.UnitOfWork.RunInTransactionAsync(s.Operation, s.State, token),
                verifySucceeded: null,
                ct).ConfigureAwait(false);
        }
        catch (RetryLimitExceededException ex)
        {
            throw new PersistenceException("The operation failed after the execution strategy exhausted its retries.", ex);
        }
        catch (Exception ex) when (FindUnwrapped(ex) is { } original)
        {
            // The strategy gave up on a failure it did not retry: surface the original translation.
            ExceptionDispatchInfo.Throw(original);
            throw; // unreachable
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // The operation flushed through the DbContext itself rather than CommitAsync.
            throw new PersistenceException("Optimistic concurrency conflict during the transaction.", ex);
        }
        catch (DbUpdateException ex)
        {
            // The operation flushed through the DbContext itself rather than CommitAsync.
            throw new PersistenceException("Database error during the transaction.", ex);
        }
        catch (DbException ex)
        {
            // A failure of the transaction itself (BEGIN, or COMMIT — e.g. a deferred constraint),
            // or of raw SQL the operation ran, that the strategy did not retry.
            throw new PersistenceException("Database error during the transaction.", ex);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is DbException or DbUpdateException)
        {
            // A provider's non-retrying strategy wraps a failure it classifies as transient.
            throw new PersistenceException(
                "Database error during the transaction. The provider classified it as transient; enable retry on failure to retry it.",
                ex);
        }
        finally
        {
            _unwrapped = null;
        }
    }

    private async Task<TResult> RunInTransactionAsync<TState, TResult>(
        Func<TState, CancellationToken, Task<TResult>> operation,
        TState state,
        CancellationToken ct)
    {
        var transaction = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        TResult result;
        try
        {
            result = await operation(state, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (PersistenceException ex) when (ex.InnerException is DbUpdateException dbUpdate)
        {
            await AbandonAttemptAsync(transaction).ConfigureAwait(false);
            _unwrapped = ex;
            ExceptionDispatchInfo.Throw(dbUpdate); // let the strategy classify the EF Core exception
            throw; // unreachable
        }
        catch
        {
            await AbandonAttemptAsync(transaction).ConfigureAwait(false);
            throw;
        }

        await transaction.DisposeAsync().ConfigureAwait(false);
        return result;
    }

    // Runs while an exception is in flight, so nothing here may replace it.
    // - Rollback with CancellationToken.None: the caller's token may be what cancelled the
    //   operation, and a cancelled rollback would surface an OperationCanceledException instead.
    // - Rollback and dispose failures are swallowed: EF Core logs a provider rollback failure as
    //   RelationalEventId.TransactionError, and the database rolls an uncommitted transaction back
    //   anyway when the transaction is disposed or its connection closes.
    // - The change tracker is cleared: the database state was rolled back, so what the attempt left
    //   tracked (including changes SaveChanges already accepted before a failed COMMIT) no longer
    //   matches it. A retried attempt therefore starts from a clean tracker and re-reads the rows.
    private async Task AbandonAttemptAsync(IDbContextTransaction transaction)
    {
#pragma warning disable CA1031 // never mask the in-flight exception
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        try
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
#pragma warning restore CA1031

        ClearTracker();
    }

    // Clears the change tracker and, when UseMicroKitPersistence() configured the context, the record
    // of the roots loaded whole: Clear() raises no event the record could observe.
    private void ClearTracker()
    {
        context.ChangeTracker.Clear();
        KeyLookup.FindLoadRecord(context)?.Reset();
    }

    // The escaping exception is the DbUpdateException we unwrapped, or a provider wrapper around it.
    private PersistenceException? FindUnwrapped(Exception escaping)
    {
        if (_unwrapped?.InnerException is not { } unwrappedCause)
            return null;

        for (var current = escaping; current is not null; current = current.InnerException)
        {
            if (ReferenceEquals(current, unwrappedCause))
                return _unwrapped;
        }

        return null;
    }
}
