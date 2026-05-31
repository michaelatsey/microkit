namespace MicroKit.Persistence.Testing;

/// <summary>
/// An in-memory <see cref="IUnitOfWork"/> test double for unit-testing command handlers
/// without a real database or EF Core context.
/// </summary>
/// <remarks>
/// <see cref="CommitAsync"/> honours cancellation and increments <see cref="CommitCount"/>,
/// but performs no real I/O. Use <see cref="CommitCount"/> in tests to assert that a
/// command handler committed exactly once.
/// </remarks>
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    /// <summary>Gets the number of times <see cref="CommitAsync"/> has completed successfully.</summary>
    public int CommitCount { get; private set; }

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
}
