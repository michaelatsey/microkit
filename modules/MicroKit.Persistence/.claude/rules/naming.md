# Rule: Naming — MicroKit.Persistence

## Repositories

| Type | Convention | Example |
|---|---|---|
| Write contract | `IRepository<TAggregate, TId>` | |
| Typed write interface | `I{Aggregate}Repository : IRepository<{Aggregate}, {Aggregate}Id>` | `IOrderRepository` |
| EF write base | `EfRepository<TAggregate, TId, TContext>` | |
| Typed EF write repository | `Ef{Aggregate}Repository` | `EfOrderRepository` |
| Read contract | `IReadRepository<TAggregate>` | |
| Typed read interface | `I{Aggregate}ReadRepository : IReadRepository<{Aggregate}>` | `IOrderReadRepository` |
| EF read base | `EfReadRepository<TAggregate, TContext>` | |
| Typed EF read repository | `Ef{Aggregate}ReadRepository` | |
| In-memory doubles | `InMemoryRepository<TAggregate, TId>`, `InMemoryReadRepository<TAggregate>` | |

## Unit of Work and Transactions

| Type | Name |
|---|---|
| Commit boundary | `IUnitOfWork` |
| Transactional execution | `ITransactionalContext` |
| EF composite | `ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext` |
| EF implementation | `EfUnitOfWork<TContext>` |
| In-memory | `InMemoryUnitOfWork` |

## Query and Pagination

| Type | Name |
|---|---|
| Options record | `QueryOptions<TAggregate>` |
| Paged result | `IPagedResult<T>` / `PagedResult<T>` |
| Page request | `PaginationOptions` |
| Page bounds | `PaginationLimits` |
| Evaluator | `ISpecificationEvaluator` / `EfSpecificationEvaluator` |

Builder extensions use the `With-` prefix (`WithIncludes`, `WithPagination`, `WithTracking`,
`WithOrdering`, `WithOrderBy`, `WithThenBy`, `WithSpec`), except `AsNoTracking()` and
`AsSplitQuery()`, named after their EF Core counterparts. No extension is named after a
`QueryOptions` property: the property would win — see `specifications.md`.

## Specifications (in MicroKit.Domain)

`{Condition}{Aggregate}[By{Discriminant}]Spec` — `OpenOrdersSpec`, `OrderByNumberSpec`.

## Analyzers

| Item | Convention | Example |
|---|---|---|
| Analyzer class | `{Concern}Analyzer` | `ReadRepositoryMutationAnalyzer` |
| Diagnostic ID | `MKP{NNN}` | `MKP001` |
| Code fix | none — the analyzers are diagnostic-only |

Registered IDs: `analyzers.md`.

## DI Entry Points

```csharp
AddMicroKitPersistence(this IServiceCollection, Action<PersistenceBuilder>?)
AddEntityFrameworkCore(this PersistenceBuilder, Action<EfCoreBuilder>?)
ConfigurePagination(this EfCoreBuilder, PaginationLimits)
AddDbContext<TContext>(this EfCoreBuilder, Action<DbContextOptionsBuilder>?)
AddUnitOfWork<TContext>(this EfCoreBuilder)
UsePostgreSQL<TContext>(this EfCoreBuilder, string, Action<NpgsqlDbContextOptionsBuilder>?, ProviderOptions?)
UseSqlServer<TContext>(this EfCoreBuilder, string, Action<SqlServerDbContextOptionsBuilder>?, ProviderOptions?)
```

## Namespaces

One namespace per project, equal to the assembly name (`NamespaceConventionTests`):
`MicroKit.Persistence.Abstractions`, `MicroKit.Persistence`, `MicroKit.Persistence.EntityFrameworkCore`
(DI, conventions and `ProviderOptions` included), `….EntityFrameworkCore.PostgreSql`,
`….EntityFrameworkCore.SqlServer`, `MicroKit.Persistence.Specifications`,
`MicroKit.Persistence.Testing`, `MicroKit.Persistence.Analyzers`.
