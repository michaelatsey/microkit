# Standard: Repository Contracts

**Canonical method signatures and constraints for all repositories.**

---

## Generic Constraint

All repositories are constrained to `IAggregateRoot` from `MicroKit.Domain.Abstractions`:

```csharp
where TAggregate : IAggregateRoot
```

This ensures only aggregate roots — the transactional boundary of the domain model — are accessed
directly through a repository. Child entities are always accessed via their owning aggregate.

---

## Canonical FindAsync Signatures

```csharp
// By strongly-typed ID (preferred)
ValueTask<TAggregate?> FindAsync(TId id, CancellationToken ct = default);

// By multiple keys (for composite PKs)
ValueTask<TAggregate?> FindAsync(object[] keyValues, CancellationToken ct = default);
```

Return type is always `TAggregate?` (nullable) — never throws `NotFoundException`. The caller
decides how to handle not-found (return `Result.Failure(new NotFoundError(...))` in the handler).

---

## Write Repository Mutation Methods

```csharp
// Stage for insertion — does NOT commit
ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default);

// Stage for update — does NOT commit
ValueTask UpdateAsync(TAggregate aggregate, CancellationToken ct = default);

// Stage for deletion — does NOT commit
ValueTask DeleteAsync(TAggregate aggregate, CancellationToken ct = default);

// Commit all staged changes
ValueTask CommitAsync(CancellationToken ct = default);
```

**Key invariant:** `AddAsync`, `UpdateAsync`, `DeleteAsync` only stage the change — they do NOT
persist. `CommitAsync` is the single persistence point per handler invocation.

---

## Read Repository Signatures

```csharp
// By ID — null if not found
ValueTask<TAggregate?> FindAsync(TId id, CancellationToken ct = default);

// Filtered list with QueryOptions (spec + includes + pagination)
ValueTask<IReadOnlyList<TAggregate>> ListAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);

// Paged list
ValueTask<IPagedResult<TAggregate>> ListPagedAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);

// Existence check
ValueTask<bool> AnyAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);

// Count
ValueTask<int> CountAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
```

**All read methods:**
- Return `IReadOnlyList<T>` not `List<T>` or `IQueryable<T>`
- Never expose change tracking
- Never call `CommitAsync` or any mutation method

---

## Custom Repository Extension Pattern

```csharp
// ✅ Typed extension of IRepository for aggregate-specific queries
public interface IUserRepository : IRepository<User>
{
    ValueTask<User?> FindByEmailAsync(Email email, CancellationToken ct = default);
    ValueTask<bool> ExistsByEmailAsync(Email email, CancellationToken ct = default);
}

// ✅ EF Core implementation
public sealed class EfUserRepository(AppDbContext ctx, IUnitOfWork uow)
    : EfRepository<User, AppDbContext>(ctx, uow), IUserRepository
{
    public async ValueTask<User?> FindByEmailAsync(Email email, CancellationToken ct)
        => await _ctx.Users
            .FirstOrDefaultAsync(u => u.Email == email, ct)
            .ConfigureAwait(false);
}
```

---

## IRepository vs IReadRepository Decision Table

| Scenario | Interface |
|---|---|
| Command handler needs to load and save an aggregate | `IRepository<T>` |
| Query handler needs to read data for display | `IReadRepository<T>` |
| Custom finder query (business-logic filter) | `IRepository<T>` custom method |
| Projection / DTO read with server-side select | `IReadRepository<T>` custom method |
| Pagination | `IReadRepository<T>.ListPagedAsync` |
| Existence check in a command handler | `IRepository<T>.FindAsync` + null check |
| Existence check in a validation rule | `IReadRepository<T>.AnyAsync` |
