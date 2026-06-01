# Changelog — MicroKit.Persistence

## [1.0.0-preview.1] — 2026-06-01

### Packages Released

- `MicroKit.Persistence.Abstractions`
- `MicroKit.Persistence`
- `MicroKit.Persistence.EntityFrameworkCore`
- `MicroKit.Persistence.EntityFrameworkCore.PostgreSql`
- `MicroKit.Persistence.EntityFrameworkCore.SqlServer`
- `MicroKit.Persistence.Specifications`
- `MicroKit.Persistence.Testing`
- `MicroKit.Persistence.Analyzers`

### Added

#### MicroKit.Persistence.Abstractions
- `IRepository<TAggregate>` — write-side repository contract with `FindAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `CommitAsync` (Unit of Work boundary)
- `IReadRepository<TAggregate>` — read-side repository contract with `FindAsync`, `ListAsync`, `AnyAsync`, `CountAsync` (no mutations, analyzer-enforced)
- `IUnitOfWork` — single-method commit boundary (`CommitAsync`), moved from `MicroKit.Domain` (ADR-001)
- `ITransactionalContext` + `ITransaction` + `ITransactionManager` — ambient transaction lifecycle contracts
- `IPagedResult<T>` — paged result contract with `Items`, `TotalCount`, `Page`, `PageSize`, `TotalPages`, `HasNextPage`, `HasPreviousPage`
- `PersistenceException` — typed exception wrapping provider-level failures

#### MicroKit.Persistence (Core)
- `QueryOptions<TAggregate>` — separates WHAT (Specification) from HOW (includes, tracking, pagination, split queries) per ADR-002
- `ISpecificationEvaluator` — applies `QueryOptions<T>` to `IQueryable<T>`; lives in Core, not Abstractions (ADR-003)
- `PagedResult<T>` — `IPagedResult<T>` sealed record implementation
- `PaginationOptions` — Page + PageSize value record with computed Skip
- `IOutboxStore` — stores `INotification` (MediatR.Contracts) domain/integration events for outbox pattern
- `TransactionBehavior` integration support via `ITransactionalContext` (from Abstractions)

#### MicroKit.Persistence.EntityFrameworkCore
- `EfRepository<TAggregate, TContext>` — `IRepository<T>` EF Core implementation with `AsTracking`, change-tracker-aware writes
- `EfReadRepository<TAggregate, TContext>` — `IReadRepository<T>` EF Core implementation; all queries enforced `AsNoTracking`
- `EfUnitOfWork<TContext>` — `ITransactionalUnitOfWork` implementation wrapping `DbContext.SaveChangesAsync` + `Database.BeginTransactionAsync`
- `EfSpecificationEvaluator` — canonical `ISpecificationEvaluator` applying criteria → includes → split → order → paginate
- `ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext` — EF Core composite (ADR-004); not in Abstractions
- `AddMicroKitPersistence` + `AddEntityFrameworkCore` DI extension methods with builder pattern

#### MicroKit.Persistence.EntityFrameworkCore.PostgreSql
- `UsePostgreSQL(string connectionString)` extension on `EfCoreBuilder`
- Npgsql provider configuration (retry-on-failure, connection resiliency)

#### MicroKit.Persistence.EntityFrameworkCore.SqlServer
- `UseSqlServer(string connectionString)` extension on `EfCoreBuilder`
- SqlServer provider configuration (retry-on-failure, connection resiliency)

#### MicroKit.Persistence.Specifications
- `WithOrderBy` / `WithOrderByDescending` / `WithThenBy` / `WithThenByDescending` — ordering extensions on `QueryOptions<T>` (With- prefix avoids delegate-vs-extension collision with the `OrderBy` property)
- `WithSpec` — specification-swap extension
- Specification composition re-exported from `MicroKit.Domain`: `And`, `Or`, `Not`

#### MicroKit.Persistence.Testing
- `InMemoryRepository<TAggregate>` — in-memory `IRepository<T>` for unit tests; no EF Core required
- `InMemoryReadRepository<TAggregate>` — in-memory `IReadRepository<T>` with spec criteria evaluation via LINQ
- `InMemoryUnitOfWork` — `IUnitOfWork` test double; tracks `CommitAsync` call count
- Full Shouldly + NSubstitute test coverage (46/46 tests)

#### MicroKit.Persistence.Analyzers
- `MKP001` (Error) — `IReadRepository<T>` implementation calls `CommitAsync` or `SaveChangesAsync`
- `MKP002` (Error) — `IReadRepository<T>` implementation declares or calls a write method (`AddAsync`, `UpdateAsync`, `DeleteAsync`)
- `MKP003` (Warning) — `SaveChangesAsync` / `SaveChanges` called directly on `DbContext` (bypasses `IUnitOfWork`)
- `MKP004` (Warning) — `DbContext` injected as constructor parameter outside infrastructure namespace
- `MKP005` (Error) — Repository method exposes `IQueryable<T>` as return type (leaks EF internals to app layer)
- `PersistenceSymbolHelper` with null-guard convention (safe against compilations missing the Persistence packages)
- Build-time only package (`analyzers/dotnet/cs/` — no `lib/` folder); 26/26 tests

#### MicroKit.Persistence.ArchitectureTests
- `LayerDependencyTests` (20 tests) — layer isolation: EF Core confined to EfCore/providers; Npgsql/SqlServer confined to their provider; `MediatR.Contracts` in Abstractions only; sibling isolation
- `NamespaceConventionTests` (9 tests) — correct namespaces per assembly across all 7 assemblies + both providers; static extension classes in correct namespaces
- `SealedClassTests` (10 tests) — `TypeAttributes.Sealed` for open-generic types (`EfUnitOfWork<>`, `EfRepository<,>`); non-sealed base confirmation
- `ContractPlacementTests` (15 tests) — ADR-001 through ADR-004 enforced at architecture level; `IReadRepository` marker in Abstractions + full contract in Core; `IOutboxStore` placement
- `ReadRepositoryPurityTests` (5 tests) — marker interface has 0 methods; Core interface has no mutations; `IQueryable<T>` not exposed on `InMemoryRepository`, `InMemoryReadRepository`, `EfReadRepository`
- Total: 59/59 tests

### Architecture Decisions

- **ADR-001** — `IUnitOfWork` moved from `MicroKit.Domain` to `MicroKit.Persistence.Abstractions`. Committing is an infrastructure concern. Breaking change for consumers importing `IUnitOfWork` from `MicroKit.Domain`.
- **ADR-002** — `QueryOptions<T>` separates WHAT (`Specification<T>` — domain) from HOW (includes, tracking, pagination — application). `Specification<T>` contains criteria only; no `Include`, no pagination.
- **ADR-003** — Abstractions minimality rule. `MicroKit.Persistence.Abstractions` contains only what a consuming module needs to compile without EF Core. `ISpecificationEvaluator` and `QueryOptions<T>` live in Core.
- **ADR-004** — `ITransactionalUnitOfWork` is the EF Core composite (`IUnitOfWork + ITransactionalContext`). It is declared in `EntityFrameworkCore`, not in Abstractions. Provider-agnostic code injects the two interfaces separately.

### Test Summary

| Suite | Tests |
|-------|-------|
| `MicroKit.Persistence.UnitTests` | (integration of in-memory repos + QueryOptions) |
| `MicroKit.Persistence.Analyzers.Tests` | 26/26 |
| `MicroKit.Persistence.ArchitectureTests` | 59/59 |
| Total (non-integration) | 131+ |

### Notes — Deferred to v1.1.0

- **DN-001** — Execution strategy gap in `EfUnitOfWork.CommitAsync`: direct `CommitAsync` calls (bypassing `TransactionBehavior`) do not retry on transient failures for cloud databases (Azure SQL, Neon). The `TransactionBehavior` path via `ExecuteAsync` is fully protected. For v1.1.0: wrap `CommitAsync` in `CreateExecutionStrategy()` with a guard against nested strategies when a transaction is already active.
- **DN-002** — `ListPagedAsync` fallback `PageSize: 1` when `totalCount == 0`: the paging math is correct (`TotalPages = 0`) but consumers inspecting `PageSize` on an empty unpaginated result may find `PageSize = 1` confusing. Risk is low. For v1.1.0: consider `PagedResult.Empty<T>()` factory or a `<remarks>` note on `ListPagedAsync`.
