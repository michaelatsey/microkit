# MicroKit.Persistence

Repository, unit-of-work and transaction contracts for DDD aggregates, with an EF Core
implementation for PostgreSQL and SQL Server, MicroKit.Domain type mappings, in-memory test doubles
and Roslyn analyzers. Targets .NET 10 and EF Core 10.

Every C# sample below is compiled from
[`samples/MicroKit.Persistence.Samples`](samples/MicroKit.Persistence.Samples), which builds in
Release with the module; `ReadmeSamplesTests` fails if a sample drifts from it.

## Packages

| Package | Contents | Depends on |
|---|---|---|
| `MicroKit.Persistence.Abstractions` | `IRepository<TAggregate, TId>`, `IUnitOfWork`, `ITransactionalContext`, `IPagedResult<T>`, `PersistenceException` | `MicroKit.Domain` |
| `MicroKit.Persistence` | `IReadRepository<TAggregate>`, `QueryOptions<TAggregate>`, pagination, `ISpecificationEvaluator` | Abstractions |
| `MicroKit.Persistence.EntityFrameworkCore` | EF Core repositories, unit of work, DI registration, Domain conventions | Core, EF Core |
| `MicroKit.Persistence.EntityFrameworkCore.PostgreSql` | `UsePostgreSQL` | EntityFrameworkCore, Npgsql |
| `MicroKit.Persistence.EntityFrameworkCore.SqlServer` | `UseSqlServer` | EntityFrameworkCore, SQL Server provider |
| `MicroKit.Persistence.Specifications` | Ordering and specification-swap extensions for `QueryOptions` | Core |
| `MicroKit.Persistence.Testing` | In-memory repository and unit-of-work doubles | Core |
| `MicroKit.Persistence.Analyzers` | MKP001–MKP005, build-time only | — |

## Composition

Register the provider, the unit of work and the repositories once:

```csharp
services.AddMicroKitPersistence(persistence => persistence
    .AddEntityFrameworkCore(ef => ef
        .UsePostgreSQL<ShopDbContext>(connectionString, options: new ProviderOptions
        {
            Retry = new RetryOnFailureOptions(maxRetryCount: 5),
        })
        .AddUnitOfWork<ShopDbContext>()
        .ConfigurePagination(new PaginationLimits(defaultPageSize: 20, maxPageSize: 100))));

services.AddScoped<IOrderRepository, EfOrderRepository>();
services.AddScoped<IReadRepository<Order>, EfReadRepository<Order, ShopDbContext>>();
return services;
```

`AddUnitOfWork<TContext>()` binds one scoped `EfUnitOfWork<TContext>` to `IUnitOfWork`,
`ITransactionalContext` and `ITransactionalUnitOfWork`, and registers `EfDomainEventsProvider<TContext>`
as the scoped `IDomainEventsProvider`. `MicroKit.MediatR`'s `TransactionBehavior` consumes them: it
runs each command inside `ITransactionalContext.ExecuteAsync`, dispatches the domain events, then
calls `IUnitOfWork.CommitAsync`.

`ProviderOptions` is off by default:

- `Retry` — a `RetryOnFailureOptions(maxRetryCount = 6, maxRetryDelay = 30 s)` turns on the provider's
  retrying execution strategy. Open transactions through `ITransactionalContext.ExecuteAsync`, which
  runs them inside the execution strategy, never with `Database.BeginTransactionAsync` directly: the
  retrying strategy rejects a transaction opened outside it, and the next query or save on that context
  throws `InvalidOperationException` ([connection
  resiliency](https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency#execution-strategies-and-transactions)).
  Register each context once: EF Core applies every registration's configuration in order, so a second
  `UsePostgreSQL`, `UseSqlServer` or `AddDbContext` for the same context merges into the first (the
  last connection string wins), and a `PoolSize` given after a plain registration does not pool the
  context.
- `PoolSize` — registers the context with `AddDbContextPool`. A pooled context's constructor-injected
  scoped services are resolved once from the root provider and shared by every lease ("scoped services
  injected into the context will only be resolved once from the initial scope", [`AddDbContextPool`
  remarks](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dependencyinjection.entityframeworkservicecollectionextensions.adddbcontextpool?view=efcore-10.0)),
  and EF Core does not reject such a context. Tenant-aware contexts such as Tenancy's
  `MultitenantDbContext` (whose constructor also takes an `ITenantContextAccessor`) must not be pooled,
  so with `PoolSize` set, `UsePostgreSQL` and `UseSqlServer` throw `ArgumentException` at registration
  unless the context's only public constructor takes just `DbContextOptions` or
  `DbContextOptions<TContext>`.

## The domain model

Aggregates come from `MicroKit.Domain`. The repositories need no base class beyond `IAggregateRoot`;
an `EntityId<T>` identifier and `AuditableAggregateRoot<TId>` are used here:

```csharp
public sealed record OrderId : EntityId<OrderId>
{
    public OrderId(Guid value) : base(value) { }

    public static OrderId New() => new(Guid.CreateVersion7());
}

public sealed class Order : AuditableAggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = [];

    // Used by EF Core: the audit fields are written through their backing fields.
    private Order(OrderId id, string number) : base(id, DateTimeOffset.UnixEpoch, Actor.Migration) => Number = number;

    private Order(OrderId id, string number, Actor placedBy, TimeProvider clock) : base(id, clock, placedBy) => Number = number;

    public string Number { get; private set; }

    public bool IsClosed { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public static Order Place(string number, Actor placedBy, TimeProvider clock) =>
        new(OrderId.New(), number, placedBy, clock);

    public void AddLine(string sku, int quantity, Actor by, TimeProvider clock) =>
        Mutate(clock.GetUtcNow(), by, () => _lines.Add(new OrderLine(Guid.CreateVersion7(), sku, quantity)));

    public void Close(Actor by, TimeProvider clock) => Mutate(clock.GetUtcNow(), by, () => IsClosed = true);
}

public sealed class OrderLine(Guid id, string sku, int quantity)
{
    public Guid Id { get; private set; } = id;

    public string Sku { get; private set; } = sku;

    public int Quantity { get; private set; } = quantity;
}
```

## The DbContext

`AddDomainConventions` maps the `MicroKit.Domain` types without hand-written converters:

```csharp
public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddDomainConventions(typeof(OrderId).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.Ignore(o => o.DomainEvents);
            order.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId");
        });

        // The domain assigns the line's Guid key: without this, EF Core treats a Guid key as
        // generated and tracks a new line with a key already set as Modified, not Added.
        modelBuilder.Entity<OrderLine>().Property(l => l.Id).ValueGeneratedNever();
    }
}
```

| Convention | Maps |
|---|---|
| `AddEntityIdConventions(assemblies)` | every `record X : EntityId<X>` of the assemblies, and `GuidId`, to a `Guid` column, read back through the `(Guid)` constructor whatever its accessibility; model building fails, naming the type, when there is none |
| `AddActorConvention()` | `Actor` to a string of at most `Actor.MaxLength` (200), read back through `Actor.Restore` |
| `AddAuditInstantConvention()` | `CreatedAt` / `UpdatedAt` of every `IAuditableEntity` normalised to offset zero; a stored `0001-01-01` is rejected |

A key whose CLR type is `Guid` is generated by EF Core by default. A child entity whose `Guid` key
the domain assigns, such as `OrderLine` here, needs `ValueGeneratedNever()`: otherwise a new child
with its key already set is tracked as `Modified` and updated, not inserted ([EF Core 3.0 breaking
changes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-3.x/breaking-changes#detectchanges-honors-store-generated-key-values)).
`EntityId<T>` keys need nothing: an identifier type is never generated.

A conversion configured explicitly in `OnModelCreating` wins. Compiled models
(`dotnet ef dbcontext optimize`) and NativeAOT precompiled queries are not supported by these
conventions.

## The write side

`IRepository<TAggregate, TId>` is the only write-side contract: `FindAsync`, `AddAsync`, `DeleteAsync`.
It never commits — `IUnitOfWork` does — and has no update method: an aggregate it returns is tracked.

```csharp
public interface IOrderRepository : IRepository<Order, OrderId>
{
    ValueTask<Order?> FindByNumberAsync(string number, CancellationToken ct = default);
}

public sealed class EfOrderRepository(ShopDbContext context)
    : EfRepository<Order, OrderId, ShopDbContext>(context), IOrderRepository
{
    public async ValueTask<Order?> FindByNumberAsync(string number, CancellationToken ct = default)
    {
        var orders = await LoadAggregatesAsync(o => o.Number == number, ct).ConfigureAwait(false);
        return orders.SingleOrDefault();
    }
}
```

`EfRepository.FindAsync(id)`, and `LoadAggregatesAsync(predicate)` for a typed finder:

- load the **whole aggregate**, tracked, as the model's boundary defines it (below); owned entities
  come with it. A finder states which roots it wants, never what is loaded with them: there is no
  include to add and no query to shape;
- return a root a repository over the same context already loaded whole or staged with `AddAsync`
  (committed or not), or any `Added` root, as is — `FindAsync` without a query;
- **refuse** any other root the context tracks, with an `InvalidOperationException` naming its type:
  a root tracked by a direct `DbSet` query, a filtered `Include`, an explicit load, an `Attach` or
  `Update`, or a reattachment after `ChangeTracker.Clear()` may be partial, and a partial aggregate
  must never reach a command;
- split the load (`AsSplitQuery`) when the aggregate has two or more collections. A split load runs one
  statement per collection: under Read Committed they can see different snapshots, so protect an
  invariant spanning root and children with a concurrency token or a stricter isolation level.

### The aggregate boundary

The boundary lives in the model, and only there. By default it is every navigation to an entity that
does not implement `IAggregateRoot`, recursively — derived types and many-to-many included; another
aggregate root is never part of it. Mark a navigation to reference data `IsOutsideAggregate()`: it is
neither loaded with the aggregate nor counted as part of it.

```csharp
modelBuilder.Entity<CatalogItem>(item =>
{
    item.Ignore(i => i.DomainEvents);
    item.HasOne(i => i.Origin).WithMany().HasForeignKey(i => i.OriginCode);
    item.Navigation(i => i.Origin).IsOutsideAggregate(); // reference data: never loaded with the item
});
```

Model building fails, naming the types, when an entity that is not an aggregate root lies inside the
boundary of two roots — directly, or through a base type and one of its derived types. Each ownership
of an owned type is its own entity type, so the same owned type under two roots is not shared.

The boundary validation and the record of the roots a repository loaded whole come with
`UseMicroKitPersistence()`. `AddDbContext`, `UsePostgreSQL` and `UseSqlServer` call it; a context
configured another way calls it itself, or `EfRepository` refuses to load:

```csharp
new DbContextOptionsBuilder<ShopDbContext>()
    .UseNpgsql(connectionString)
    .UseMicroKitPersistence()
    .Options;
```

A root is recorded once the load that returned it has completed — a load that fails midway records
nothing, and a root the model auto-includes without its own aggregate is never recorded. The record
follows the change tracker: `IUnitOfWork.DiscardChanges()` and a failed `ExecuteAsync` attempt reset
it, and a pooled context resets it when it returns to the pool. An owned navigation cannot be marked
`IsOutsideAggregate()`: an owned entity is always loaded with its owner.

## Transactions

`ITransactionalContext.ExecuteAsync` runs an operation in a transaction:

- with no transaction open, it begins one inside the provider's execution strategy, commits on success
  and rolls back on failure. A transient failure of the commit is retried — the whole operation runs
  again, so it must be idempotent, and it must load what it modifies (the change tracker is cleared after
  a failed attempt);
- with a transaction already open — an enclosing `ExecuteAsync`, or one the caller opened — it joins it:
  no strategy, no begin, commit or rollback;
- the rollback never runs with a cancelled token and never replaces the original exception;
- the outermost call surfaces every database failure as a `PersistenceException`; a joined call lets
  the operation's exceptions through to the transaction's owner.

### A command without MediatR

```csharp
transactions.ExecuteAsync(
    static async (s, token) =>
    {
        var order = await s.Orders.FindAsync(s.Id, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Order {s.Id} does not exist.");

        order.AddLine(s.Sku, s.Quantity, s.By, s.Clock);
        await s.UnitOfWork.CommitAsync(token).ConfigureAwait(false);
    },
    (Orders: orders, UnitOfWork: unitOfWork, Clock: clock, Id: id, Sku: sku, Quantity: quantity, By: by),
    ct);
```

## The read side

`IReadRepository<TAggregate>` queries through `QueryOptions<TAggregate>`, which pairs a domain
`Specification<T>` (what) with includes, split query, ordering and pagination (how). The read side
never tracks: there is no tracking option, and every method returns new, untracked instances.

```csharp
public sealed class OpenOrdersSpec : Specification<Order>
{
    public override System.Linq.Expressions.Expression<Func<Order, bool>> ToExpression() => order => !order.IsClosed;
}

public sealed class ListOpenOrders(IReadRepository<Order> orders)
{
    public ValueTask<IPagedResult<Order>> HandleAsync(int page, CancellationToken ct = default) =>
        orders.ListPagedAsync(
            new QueryOptions<Order>(new OpenOrdersSpec())
                .WithOrderByDescending(o => o.CreatedAt)
                .WithThenBy(o => o.Id) // a unique tie-breaker: stable page boundaries
                .WithPagination(page, pageSize: 20),
            ct);
}
```

- `ListPagedAsync` without pagination returns the first page of `PaginationLimits.DefaultPageSize`
  items (50 unless configured), never the whole table; a page larger than `MaxPageSize` (500) is
  rejected. `ListAsync` without pagination returns every match.
- `FindAsync(keyValues)` and the list methods never consult the change tracker: an aggregate the
  command tracks in the same context comes back as a new, untracked instance, and its pending change
  is not visible through it. Load what a command modifies through `IRepository<TAggregate, TId>`.
- Ordering is required for stable pages.

## Testing without a database

```csharp
var orders = new InMemoryRepository<Order, OrderId>();
var unitOfWork = new InMemoryUnitOfWork();
var clock = TimeProvider.System;
var order = Order.Place("SO-1", new Actor("user-1"), clock);

await orders.AddAsync(order);
(await orders.FindAsync(order.Id))!.AddLine("BOOK", 1, new Actor("user-1"), clock);
await unitOfWork.CommitAsync();
```

`InMemoryRepository<TAggregate, TId>` implements both repository contracts on one store and enforces the
same pagination limits as the EF Core repository.

## Analyzers

| ID | Severity | Rule |
|---|---|---|
| MKP001 | Error | A read repository calls `CommitAsync` or `SaveChangesAsync` |
| MKP002 | Error | A read repository declares or calls a write method |
| MKP003 | Warning | `SaveChanges` called directly on a `DbContext`, bypassing `IUnitOfWork` |
| MKP004 | Warning | A `DbContext` injected outside the infrastructure layer |
| MKP005 | Error | A repository method returns `IQueryable<T>` |

## Public types

| Type | Package | Role |
|---|---|---|
| `IRepository<TAggregate, TId>` | Abstractions | Write side: whole-aggregate load, add, delete |
| `IUnitOfWork` | Abstractions | Commit or discard the pending change set |
| `ITransactionalContext` | Abstractions | Run an operation in a transaction (retry, join) |
| `IPagedResult<T>` | Abstractions | A page of items with the total count |
| `PersistenceException` | Abstractions | Every database failure surfaced by the module |
| `IReadRepository<TAggregate>` | Core | Read side, untracked: find, list, page, any, count |
| `QueryOptions<TAggregate>` | Core | Specification + includes, split query, ordering, pagination |
| `QueryOptionsExtensions` | Core | `WithIncludes`, `WithPagination`, `AsSplitQuery`, `WithOrdering` |
| `PaginationOptions` | Core | A validated page request |
| `PaginationLimits` | Core | Default and maximum page sizes |
| `PagedResult<T>` | Core | `IPagedResult<T>` implementation |
| `ISpecificationEvaluator` | Core | Applies `QueryOptions` to an `IQueryable<T>` |
| `EfRepository<TAggregate, TId, TContext>` | EntityFrameworkCore | EF Core write-side base; `LoadAggregatesAsync` for typed finders |
| `EfReadRepository<TAggregate, TContext>` | EntityFrameworkCore | EF Core read-side base |
| `EfUnitOfWork<TContext>` | EntityFrameworkCore | `IUnitOfWork` + `ITransactionalContext` over a `DbContext` |
| `ITransactionalUnitOfWork` | EntityFrameworkCore | The two contracts as one service |
| `EfSpecificationEvaluator` | EntityFrameworkCore | EF Core `ISpecificationEvaluator` |
| `EfDomainEventsProvider<TContext>` | EntityFrameworkCore | Drains the domain events of every tracked aggregate |
| `PersistenceServiceCollectionExtensions` | EntityFrameworkCore | `AddMicroKitPersistence`, `AddEntityFrameworkCore`, `AddDbContext`, `AddUnitOfWork`, `ConfigurePagination` |
| `PersistenceBuilder`, `EfCoreBuilder` | EntityFrameworkCore | Registration builders |
| `ProviderOptions`, `RetryOnFailureOptions` | EntityFrameworkCore | Opt-in retry and pooling |
| `DomainConventionExtensions` | EntityFrameworkCore | MicroKit.Domain mappings for `ConfigureConventions` |
| `MicroKitPersistenceDbContextOptionsExtensions` | EntityFrameworkCore | `UseMicroKitPersistence()`: boundary validation and the loaded-root record |
| `AggregateBoundaryNavigationBuilderExtensions` | EntityFrameworkCore | `IsOutsideAggregate()` on a navigation |
| `PostgreSqlEfCoreBuilderExtensions` | PostgreSql | `UsePostgreSQL<TContext>` |
| `SqlServerEfCoreBuilderExtensions` | SqlServer | `UseSqlServer<TContext>` |
| `QueryOptionsOrderingExtensions` | Specifications | `WithOrderBy`, `WithOrderByDescending`, `WithThenBy`, `WithThenByDescending` |
| `QueryOptionsSpecificationExtensions` | Specifications | `WithSpec` |
| `InMemoryRepository<TAggregate, TId>` | Testing | Write and read double |
| `InMemoryReadRepository<TAggregate>` | Testing | Read-only double |
| `InMemoryUnitOfWork` | Testing | Counts commits and discards |
| `ReadRepositoryMutationAnalyzer`, `DirectSaveChangesAnalyzer`, `DbContextInjectionAnalyzer`, `RepositoryIQueryableLeakAnalyzer` | Analyzers | MKP001–MKP005 |

## Decisions

Architecture decisions live in
[`.claude-context/context/architectural-decisions.md`](.claude-context/context/architectural-decisions.md);
the breaking changes of each version in [`CHANGELOG.md`](CHANGELOG.md).
