# MicroKit.Idempotency.Abstractions

Pure idempotency contracts and model types with no external dependencies. Defines the persistence interface (`IIdempotencyStore`), request marker (`IIdempotentRequest<TResponse>`), ambient context interfaces, request hasher, and the `IdempotencyState` data model. Infrastructure and handler packages depend on this; application code uses `IIdempotentRequest<T>` to opt requests into deduplication.

## When to use

Reference `MicroKit.Idempotency.Abstractions` in:
- Command definitions that must be idempotent (implement `IIdempotentRequest<TResponse>`)
- Custom `IIdempotencyStore` implementations
- Any package that reads the current idempotency state via `IIdempotencyAccessor`

Reference `MicroKit.Idempotency.EFCore` or `MicroKit.Idempotency.Redis` for concrete stores, and `MicroKit.Idempotency.MediatR` to plug the behavior into the pipeline.

## Installation

```
dotnet add package MicroKit.Idempotency.Abstractions
```

## Key types

| Type | Description |
|---|---|
| `IIdempotencyStore` | `GetAsync`, `CreateAsync`, `CompleteAsync`, `FailAsync`, `RenewExpirationAsync`, `DeleteAsync` |
| `IIdempotentRequest` | Marker; exposes `IdempotencyKey` and optional `IdempotencyExpiration` |
| `IIdempotentRequest<TResponse>` | Typed variant for commands that return a value |
| `IIdempotencyAccessor` | Read-only access to the current key: `CurrentKey`, `IsIdempotent` |
| `IIdempotencyManager` | Extends `IIdempotencyAccessor` with `SetKey()` and `Clear()` for pipeline behavior use |
| `IIdempotencyContext` | Scoped context; `BeginScope(key)` returns a disposable that ends the scope; `UpdateState()` updates the cached state |
| `IRequestHasher` | `ComputeHash<T>(request)` for detecting payload mismatches on duplicate keys |
| `IIdempotencyCleanupService` | `CleanupAsync(olderThan, status, batchSize)` for scheduled record purging |
| `IdempotencyState` | Record: `Key`, `TenantId`, `Status`, `Response`, `RequestHash`, `CreatedAtUtc`, `CompletedAtUtc` |
| `IdempotencyStatus` | `Processing`, `Completed`, `Failed`, `Cancelled` |

## Usage

```csharp
// Mark a command as idempotent
public record CreateInvoiceCommand(Guid OrderId, string IdempotencyKey)
    : ICommand<Guid>, IIdempotentRequest<Guid>
{
    // IIdempotentRequest<Guid>
    string IIdempotentRequest.IdempotencyKey => IdempotencyKey;
    TimeSpan? IIdempotentRequest.IdempotencyExpiration => TimeSpan.FromHours(24);
}
```

When the `IdempotencyBehavior` intercepts this command:
- A new request creates a `Processing` record, executes the handler, then marks it `Completed` with the serialized response.
- A duplicate key with `Completed` status returns the cached response immediately without re-executing the handler.
- A duplicate key with `Processing` status throws `IdempotencyProgressingException`.

## Dependencies

None.
