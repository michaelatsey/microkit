# Standard: Abstractions Contracts

**Canonical types in `MicroKit.Persistence.Abstractions`.**

---

## IRepository<TAggregate>

```csharp
namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Write-side repository for <typeparamref name="TAggregate"/> aggregates.
/// Provides CRUD operations and the Unit of Work commit boundary.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
public interface IRepository<TAggregate>
    where TAggregate : IAggregateRoot
{
    /// <summary>Finds an aggregate by its primary key.</summary>
    ValueTask<TAggregate?> FindAsync(/* strongly-typed Id */, CancellationToken ct = default);

    /// <summary>Stages a new aggregate for insertion.</summary>
    ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default);

    /// <summary>Stages an existing aggregate for update.</summary>
    ValueTask UpdateAsync(TAggregate aggregate, CancellationToken ct = default);

    /// <summary>Stages an aggregate for deletion.</summary>
    ValueTask DeleteAsync(TAggregate aggregate, CancellationToken ct = default);

    /// <summary>
    /// Commits all pending changes to the underlying store.
    /// </summary>
    /// <exception cref="PersistenceException">Thrown when the provider fails to commit.</exception>
    ValueTask CommitAsync(CancellationToken ct = default);
}
```

## IReadRepository<TAggregate>

```csharp
/// <summary>
/// Read-side repository for <typeparamref name="TAggregate"/> aggregates.
/// Never mutates state. Always queries without change tracking.
/// </summary>
public interface IReadRepository<TAggregate>
    where TAggregate : IAggregateRoot
{
    ValueTask<TAggregate?> FindAsync(/* Id */, CancellationToken ct = default);
    ValueTask<IReadOnlyList<TAggregate>> ListAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
    ValueTask<bool> AnyAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
    ValueTask<int> CountAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
}
```

> Note: `QueryOptions<TAggregate>` lives in Core (`MicroKit.Persistence`), not Abstractions.
> Abstractions-only consumers import Core as well for QueryOptions usage.

## IUnitOfWork

```csharp
/// <summary>
/// Defines the change-set boundary for aggregate persistence: a unit of work is either
/// committed or discarded.
/// Inject in command handlers; call <see cref="CommitAsync"/> once per command.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Commits all pending changes accumulated since the last commit.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <exception cref="PersistenceException">Thrown when the underlying provider fails to commit.</exception>
    ValueTask CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Abandons every pending change accumulated since the last commit, without writing them.
    /// </summary>
    /// <remarks>
    /// Synchronous by design — no implementation performs I/O (EF Core: <c>ChangeTracker.Clear()</c>).
    /// Called by <c>TransactionBehavior</c> at every command boundary that does not commit —
    /// business failure and thrown exception alike. See ADR-005.
    /// </remarks>
    void DiscardChanges();
}
```

## ITransactionalContext

```csharp
/// <summary>
/// Executes a database operation inside an explicit database transaction.
/// Begin, Commit, and Rollback are managed internally by the implementation.
/// Consumed by <c>TransactionBehavior</c> in MicroKit.MediatR.Behaviors.
/// </summary>
public interface ITransactionalContext
{
    /// <summary>Executes <paramref name="operation"/> inside a database transaction.</summary>
    Task ExecuteAsync<TState>(
        Func<TState, CancellationToken, Task> operation,
        TState state,
        CancellationToken ct = default);

    /// <summary>
    /// Executes <paramref name="operation"/> inside a database transaction and returns its result.
    /// </summary>
    Task<TResult> ExecuteAsync<TState, TResult>(
        Func<TState, CancellationToken, Task<TResult>> operation,
        TState state,
        CancellationToken ct = default);
}
```

> `TState` threads caller-owned state through without a closure; combined with a `static` lambda the
> hot path allocates nothing. The lifecycle is not exposed because the implementation wraps the
> operation in the provider's execution strategy, which may re-invoke it on a transient failure.

## ITransaction

```csharp
/// <summary>Represents an active database transaction.</summary>
public interface ITransaction : IAsyncDisposable
{
    Guid TransactionId { get; }
}
```

## ITransactionManager

```csharp
/// <summary>
/// Manages transaction lifecycle; allows the current transaction to be accessed across services.
/// </summary>
public interface ITransactionManager
{
    ITransaction? CurrentTransaction { get; }
}
```

## IPagedResult<T>

```csharp
/// <summary>Represents a paginated read result.</summary>
public interface IPagedResult<T>
{
    IReadOnlyList<T> Items { get; }
    int TotalCount { get; }
    int Page { get; }
    int PageSize { get; }
    int TotalPages { get; }
    bool HasNextPage { get; }
    bool HasPreviousPage { get; }
}
```

## PersistenceException

```csharp
/// <summary>
/// Thrown by repository and UoW implementations when the underlying provider encounters
/// an unrecoverable error (connection failure, constraint violation, concurrency conflict).
/// </summary>
public sealed class PersistenceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
```
