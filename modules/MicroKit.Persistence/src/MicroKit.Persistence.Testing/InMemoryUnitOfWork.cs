namespace MicroKit.Persistence.Testing;

/// <summary>
/// An in-memory <see cref="IUnitOfWork"/> test double for unit-testing command handlers
/// without a real database or EF Core context.
/// </summary>
/// <remarks>
/// <see cref="CommitAsync"/> honours cancellation and increments <see cref="CommitCount"/>,
/// but performs no real I/O. <see cref="DiscardChanges"/> increments <see cref="DiscardCount"/>.
/// Use the two counters in tests to assert which exit a command boundary took — committed
/// exactly once, or discarded exactly once.
/// </remarks>
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    /// <summary>Gets the number of times <see cref="CommitAsync"/> has completed successfully.</summary>
    public int CommitCount { get; private set; }

    /// <summary>Gets the number of times <see cref="DiscardChanges"/> has been called.</summary>
    public int DiscardCount { get; private set; }

    /// <summary>
    /// Honours cancellation by throwing <see cref="OperationCanceledException"/> when
    /// <paramref name="ct"/> is already cancelled, then increments <see cref="CommitCount"/>.
    /// Returns synchronously with no allocation.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public ValueTask CommitAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        CommitCount++;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Records a discard by incrementing <see cref="DiscardCount"/>.
    /// </summary>
    /// <remarks>
    /// There is no pending change set to abandon: <see cref="InMemoryRepository{TAggregate}"/>
    /// applies writes to its store immediately and shares no state with this type. The double is
    /// therefore the no-op case ADR-005 describes for a provider that accumulates nothing — a
    /// counter, so a test can assert that a failed command discarded exactly once. Safe to call
    /// any number of times, including on an untouched instance.
    /// </remarks>
    public void DiscardChanges() => DiscardCount++;
}
