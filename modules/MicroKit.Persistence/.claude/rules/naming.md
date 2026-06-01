# Rule: Naming — MicroKit.Persistence

## Repository Contracts

| Type | Convention | Example |
|---|---|---|
| Write repository interface | `I{Entity}Repository` | `IUserRepository`, `IOrderRepository` |
| Read repository interface | `I{Entity}ReadRepository` | `IUserReadRepository`, `IOrderReadRepository` |
| EF write repository | `Ef{Entity}Repository` | `EfUserRepository`, `EfOrderRepository` |
| EF read repository | `Ef{Entity}ReadRepository` | `EfUserReadRepository` |
| In-memory (testing) | `InMemory{Entity}Repository` or `InMemoryRepository<T>` | `InMemoryUserRepository` |
| Generic base | `IRepository<T>`, `IReadRepository<T>` | (non-entity-specific) |

## Unit of Work

| Type | Convention | Example |
|---|---|---|
| Interface | `IUnitOfWork` | (single, no entity suffix) |
| EF Core implementation | `EfUnitOfWork` | |
| Composite interface | `ITransactionalUnitOfWork` | `ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext` |
| In-memory | `InMemoryUnitOfWork` | |

## Transaction

| Type | Convention | Example |
|---|---|---|
| Context interface | `ITransactionalContext` | |
| Transaction value | `ITransaction` | |
| Manager interface | `ITransactionManager` | |

## QueryOptions and Pagination

| Type | Convention | Example |
|---|---|---|
| Options record | `QueryOptions<TAggregate>` | `QueryOptions<User>` |
| Paged result interface | `IPagedResult<T>` | |
| Paged result implementation | `PagedResult<T>` | |
| Pagination parameters | `PaginationOptions` | |

## Specifications (Domain — not Persistence)

Specification types live in the **Domain** layer, but naming is documented here for consistency:

| Type | Convention | Example |
|---|---|---|
| Specification class | `{Adjective/Condition}{Entity}[By{Discriminant}]Spec` | `ActiveUserSpec`, `UserByEmailSpec` |
| Composite | `{SpecA}And{SpecB}Spec` | (use Specification.And() helper) |

## Analyzers

| Type | Convention | Example |
|---|---|---|
| Analyzer class | `{Concern}Analyzer` | `ReadRepositoryMutationAnalyzer`, `DbContextInjectionAnalyzer` |
| Diagnostic ID | `MKP{NNN}` | `MKP001`, `MKP002`, `MKP005` |
| Code fix | `{Concern}CodeFixProvider` | (none in v1 — analyzers are diagnostic-only) |

## Registered Diagnostic IDs

| ID | Severity | Description |
|----|----------|-------------|
| `MKP001` | Error | `IReadRepository<T>` implementation calls `CommitAsync` or `SaveChangesAsync` |
| `MKP002` | Error | `IReadRepository<T>` implementation declares or calls `AddAsync`, `UpdateAsync`, or `DeleteAsync` |
| `MKP003` | Warning | `SaveChangesAsync` / `SaveChanges` called directly on `DbContext` — bypasses `IUnitOfWork` |
| `MKP004` | Warning | `DbContext` injected as constructor parameter outside the infrastructure layer |
| `MKP005` | Error | Repository method exposes `IQueryable<T>` as return type |

## DI Extension Methods

```csharp
// ✅ Registration entry points
AddMicroKitPersistence(this IServiceCollection, Action<PersistenceBuilder>)
AddEntityFrameworkCore(this PersistenceBuilder, Action<EfCoreBuilder>)
UsePostgreSQL(this EfCoreBuilder, string connectionString)
UseSqlServer(this EfCoreBuilder, string connectionString)
```

## Namespaces

```csharp
namespace MicroKit.Persistence.Abstractions;           // contracts
namespace MicroKit.Persistence;                        // core
namespace MicroKit.Persistence.EntityFrameworkCore;    // EF bridge
namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql;
namespace MicroKit.Persistence.EntityFrameworkCore.SqlServer;
namespace MicroKit.Persistence.Specifications;
namespace MicroKit.Persistence.Testing;
namespace MicroKit.Persistence.Analyzers;
```
