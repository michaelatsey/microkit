# Rule: Architecture — MicroKit.Persistence

## Always active for every file in this module.

## Repository Pattern

### Write side — `IRepository<TAggregate>`
```csharp
// ✅ Aggregate root constraint — only aggregates are persisted directly
public interface IRepository<TAggregate>
    where TAggregate : IAggregateRoot
{
    ValueTask<TAggregate?> FindAsync(/* id */, CancellationToken ct = default);
    ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default);
    ValueTask UpdateAsync(TAggregate aggregate, CancellationToken ct = default);
    ValueTask DeleteAsync(TAggregate aggregate, CancellationToken ct = default);
    ValueTask CommitAsync(CancellationToken ct = default);
}

// ✅ Custom repository extends the base
public interface IUserRepository : IRepository<User>
{
    ValueTask<User?> FindByEmailAsync(Email email, CancellationToken ct = default);
}

// ❌ Persisting a non-aggregate entity directly
public interface IOrderLineRepository : IRepository<OrderLine> // ❌ OrderLine is not an aggregate root
```

### Read side — `IReadRepository<TAggregate>`
```csharp
// ✅ No mutations — reads only
public interface IReadRepository<TAggregate>
    where TAggregate : IAggregateRoot
{
    ValueTask<TAggregate?> FindAsync(/* id */, CancellationToken ct = default);
    ValueTask<IReadOnlyList<TAggregate>> ListAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
    ValueTask<bool> AnyAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
    ValueTask<int> CountAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
}

// ❌ Read repository with mutation methods
public interface IUserReadRepository : IReadRepository<User>
{
    ValueTask CommitAsync(); // ❌ read repos never commit
}
```

## Unit of Work

### IUnitOfWork — single method, infrastructure boundary
```csharp
// ✅ CommitAsync only — never SaveChangesAsync on the public interface
public interface IUnitOfWork
{
    ValueTask CommitAsync(CancellationToken ct = default);
}

// ✅ Injection in command handlers
public sealed class CreateUserHandler(IUserRepository repo, IUnitOfWork uow) { ... }

// ❌ Inject IUnitOfWork in a query handler
public sealed class GetUsersHandler(IUserReadRepository repo, IUnitOfWork uow) { ... } // ❌
```

## Transaction Context

### ITransactionalContext — ambient database transaction
```csharp
// ✅ For operations requiring atomic cross-aggregate commits
public interface ITransactionalContext : IAsyncDisposable
{
    ValueTask<ITransaction> BeginTransactionAsync(CancellationToken ct = default);
    ValueTask CommitTransactionAsync(CancellationToken ct = default);
    ValueTask RollbackTransactionAsync(CancellationToken ct = default);
}

// ✅ ITransactionalUnitOfWork (EF Core composite) — never in Abstractions
public interface ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext { }

// ✅ TransactionBehavior in MediatR.Behaviors injects ITransactionalContext
// The behavior wraps ICommand handlers — queries are not transactional
```

## QueryOptions Pattern

```csharp
// ✅ WHAT (specification, from Domain) + HOW (loading strategy, from Core)
var opts = new QueryOptions<User>(new ActiveUsersSpec())
    .WithIncludes(q => q.Include(u => u.Roles))
    .WithPagination(page: 1, pageSize: 20)
    .AsNoTracking();  // redundant on IReadRepository, required on IRepository read paths

// ❌ Specification with Include — Specification is domain-pure (criteria only)
public sealed class UserWithRolesSpec : Specification<User>
{
    public UserWithRolesSpec()
    {
        AddCriteria(u => u.IsActive);
        AddInclude(u => u.Roles); // ❌ no includes in Specification
    }
}
```

## CQRS Data-Access Alignment

| Handler Type | Inject | Forbidden |
|---|---|---|
| CommandHandler | `IRepository<T>` + `IUnitOfWork` | `IReadRepository`, `DbContext` |
| QueryHandler | `IReadRepository<T>` | `IRepository<T>`, `IUnitOfWork`, `DbContext` |
| Either | Custom typed repo extending correct base | Raw `IQueryable<T>` on public interfaces |

## Strict Rules

```
🔴 IReadRepository must not have: AddAsync, UpdateAsync, DeleteAsync, CommitAsync
🔴 SaveChangesAsync must not appear in any public method name — use CommitAsync
🔴 DbContext injected in a handler — always inject a typed repository
🔴 IRepository<T> in a QueryHandler — queries are read-only
🔴 IQueryable<T> on any IReadRepository public method signature
🟡 IUnitOfWork in MicroKit.Domain code — it moved to Persistence.Abstractions (ADR-001)
```
