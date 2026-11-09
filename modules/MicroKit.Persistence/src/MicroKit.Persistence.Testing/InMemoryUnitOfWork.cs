namespace MicroKit.Persistence.Testing;

using MicroKit.Result;

/// <summary>
/// An in-memory <see cref="IUnitOfWork"/> and <see cref="IResultUnitOfWork"/> test double for
/// unit-testing command handlers without a real database or EF Core context.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CommitAsync"/> honours cancellation and increments <see cref="CommitCount"/>,
/// but performs no real I/O. <see cref="DiscardChanges"/> increments <see cref="DiscardCount"/>.
/// Use the two counters in tests to assert which exit a command boundary took — committed
/// exactly once, or discarded exactly once.
/// </para>
/// <para>
/// The <see cref="IResultUnitOfWork"/> members follow the EF Core rules. An operation run by
/// <see cref="TryExecuteAsync{TState}"/> stands for a transaction: the commits it makes count only when
/// it succeeds, so a failed <see cref="Result"/> or an exception counts none; a version conflict of
/// <see cref="InMemoryRepository{TAggregate, TId}"/> becomes <see cref="PersistenceErrors.Concurrency"/>
/// once the operation has failed; a nested call converts nothing, and <see cref="TryCommitAsync"/> is
/// refused inside the operation. The double cannot roll its repositories back: they apply writes to their
/// store at once, so a failed operation's writes stay visible there.
/// </para>
/// <para>
/// It differs from EF Core in what it can convert. It never produces <c>DUPLICATE</c> or
/// <c>REFERENCE</c>: the double has no constraints, and adding an aggregate twice throws
/// <see cref="InvalidOperationException"/> at <c>AddAsync</c>. A version conflict is raised by
/// <c>FindAsync(id, expectedVersion)</c>, at the find, so only <see cref="TryExecuteAsync{TState}"/>
/// converts it; a handler that finds outside it and then calls <see cref="TryCommitAsync"/> gets the
/// <see cref="PersistenceException"/> from the find, where EF Core would return <c>CONCURRENCY</c> from
/// the commit.
/// </para>
/// </remarks>
public sealed class InMemoryUnitOfWork : IUnitOfWork, IResultUnitOfWork
{
    // Whether an operation of TryExecuteAsync is running (the double's "open transaction"), and the
    // commits it made so far: they count only if it succeeds. A unit of work is never used concurrently.
    private bool _inOperation;
    private int _pendingCommits;

    /// <summary>
    /// Gets the number of times <see cref="CommitAsync"/> has completed successfully, outside an operation
    /// of <see cref="TryExecuteAsync{TState}"/> or inside one that succeeded.
    /// </summary>
    public int CommitCount { get; private set; }

    /// <summary>Gets the number of times <see cref="DiscardChanges"/> has been called.</summary>
    public int DiscardCount { get; private set; }

    /// <summary>
    /// Honours cancellation by throwing <see cref="OperationCanceledException"/> when
    /// <paramref name="ct"/> is already cancelled, then increments <see cref="CommitCount"/> — or, inside an
    /// operation of <see cref="TryExecuteAsync{TState}"/>, once that operation succeeds.
    /// Returns synchronously with no allocation.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public ValueTask CommitAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_inOperation)
            _pendingCommits++;
        else
            CommitCount++;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Records a discard by incrementing <see cref="DiscardCount"/>.
    /// </summary>
    /// <remarks>
    /// There is no pending change set to abandon: <see cref="InMemoryRepository{TAggregate, TId}"/>
    /// applies writes to its store immediately and shares no state with this type. The double is
    /// therefore the no-op case ADR-005 describes for a provider that accumulates nothing — a
    /// counter, so a test can assert that a failed command discarded exactly once. Safe to call
    /// any number of times, including on an untouched instance.
    /// </remarks>
    public void DiscardChanges() => DiscardCount++;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Called inside an operation of <see cref="TryExecuteAsync{TState}"/>, the double's open transaction.
    /// </exception>
    public async ValueTask<Result> TryCommitAsync(CancellationToken ct = default)
    {
        if (_inOperation)
        {
            throw new InvalidOperationException(
                "TryCommitAsync is refused inside TryExecuteAsync: a transaction is open. Commit with CommitAsync, "
                + "and let the operation's owner convert the failure.");
        }

        await CommitAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc/>
    public async ValueTask<Result> TryExecuteAsync<TState>(
        Func<TState, CancellationToken, Task<Result>> operation,
        TState state,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_inOperation)
            return await operation(state, ct).ConfigureAwait(false);

        Begin();
        try
        {
            var result = await operation(state, ct).ConfigureAwait(false);
            if (result.IsSuccess)
                CommitCount += _pendingCommits;
            return result;
        }
        catch (PersistenceException ex) when (ex.InnerException is AggregateVersionConflictException conflict)
        {
            return Result.Failure(PersistenceErrors.Concurrency(conflict.ErrorName));
        }
        finally
        {
            End();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<Result<TValue>> TryExecuteAsync<TState, TValue>(
        Func<TState, CancellationToken, Task<Result<TValue>>> operation,
        TState state,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_inOperation)
            return await operation(state, ct).ConfigureAwait(false);

        Begin();
        try
        {
            var result = await operation(state, ct).ConfigureAwait(false);
            if (result.IsSuccess)
                CommitCount += _pendingCommits;
            return result;
        }
        catch (PersistenceException ex) when (ex.InnerException is AggregateVersionConflictException conflict)
        {
            return Result<TValue>.Failure(PersistenceErrors.Concurrency(conflict.ErrorName));
        }
        finally
        {
            End();
        }
    }

    private void Begin()
    {
        _inOperation = true;
        _pendingCommits = 0;
    }

    private void End()
    {
        _inOperation = false;
        _pendingCommits = 0;
    }
}
