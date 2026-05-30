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
/// Defines the commit boundary for aggregate persistence.
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
}
```

## ITransactionalContext

```csharp
/// <summary>
/// Provides ambient database transaction management for cross-aggregate operations.
/// Also consumed by <c>TransactionBehavior</c> in MicroKit.MediatR.Behaviors.
/// </summary>
public interface ITransactionalContext : IAsyncDisposable
{
    ValueTask<ITransaction> BeginTransactionAsync(CancellationToken ct = default);
    ValueTask CommitTransactionAsync(CancellationToken ct = default);
    ValueTask RollbackTransactionAsync(CancellationToken ct = default);
}
```

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
