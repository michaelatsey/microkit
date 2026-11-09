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
| `MicroKit.Persistence` | `IReadRepository<TAggregate>`, `QueryOptions<TAggregate>`, pagination, `ISpecificationEvaluator`, the `Result` path (`IResultUnitOfWork`, `PersistenceErrors`) | Abstractions, `MicroKit.Result` |
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
`ITransactionalContext`, `ITransactionalUnitOfWork` and `IResultUnitOfWork`, and registers `EfDomainEventsProvider<TContext>`
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
    private readonly List<TagLink> _tagLinks = [];

    // Used by EF Core: the audit fields are written through their backing fields.
    private Order(OrderId id, string number) : base(id, DateTimeOffset.UnixEpoch, Actor.Migration) => Number = number;

    private Order(OrderId id, string number, Actor placedBy, TimeProvider clock) : base(id, clock, placedBy) => Number = number;

    public string Number { get; private set; }

    public bool IsClosed { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    // Links to Tag, another aggregate: child entities of the order, loaded and versioned with it.
    public IReadOnlyList<TagLink> TagLinks => _tagLinks;

    public static Order Place(string number, Actor placedBy, TimeProvider clock) =>
        new(OrderId.New(), number, placedBy, clock);

    public void AddLine(string sku, int quantity, Actor by, TimeProvider clock) =>
        Mutate(clock.GetUtcNow(), by, () => _lines.Add(new OrderLine(Guid.CreateVersion7(), sku, quantity)));

    public void Close(Actor by, TimeProvider clock) => Mutate(clock.GetUtcNow(), by, () => IsClosed = true);

    public void Tag(TagId tag, Actor by, TimeProvider clock) =>
        Mutate(clock.GetUtcNow(), by, () => _tagLinks.Add(new TagLink(Id, tag)));

    public void Untag(TagId tag, Actor by, TimeProvider clock) =>
        Mutate(clock.GetUtcNow(), by, () => _tagLinks.RemoveAll(l => l.TagId == tag));
}

public sealed class OrderLine(Guid id, string sku, int quantity)
{
    public Guid Id { get; private set; } = id;

    public string Sku { get; private set; } = sku;

    public int Quantity { get; private set; } = quantity;
}

public sealed class TagLink(OrderId orderId, TagId tagId)
{
    public OrderId OrderId { get; private set; } = orderId;

    public TagId TagId { get; private set; } = tagId;
}

public sealed record TagId : EntityId<TagId>
{
    public TagId(Guid value) : base(value) { }
}

public sealed class Tag(TagId id, string name) : AggregateRoot<TagId>(id)
{
    public string Name { get; private set; } = name;
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
            order.HasIndex(o => o.Number).IsUnique(); // a second SO-1: PERSISTENCE.ORDER.DUPLICATE
            order.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId");
            order.HasMany(o => o.TagLinks).WithOne().HasForeignKey(l => l.OrderId);
        });

        modelBuilder.Entity<TagLink>(link =>
        {
            link.HasKey(l => new { l.OrderId, l.TagId });
            link.HasOne<Tag>().WithMany().HasForeignKey(l => l.TagId); // another aggregate: Restrict by convention
        });

        modelBuilder.Entity<Tag>().Ignore(t => t.DomainEvents);

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

`IRepository<TAggregate, TId>` is the only write-side contract: `FindAsync` (optionally at an expected
version), `AddAsync`, `DeleteAsync` and `GetVersion` (see [Optimistic concurrency](#optimistic-concurrency)).
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
  statement per collection: under Read Committed they can see different snapshots. The aggregate's
  version (see [Optimistic concurrency](#optimistic-concurrency)) makes such a torn read fail the
  commit when the command writes; a read-only decision across root and children needs Snapshot or
  Serializable isolation.

### The aggregate boundary

The boundary lives in the model, and only there. By default it is every navigation to an entity that
does not implement `IAggregateRoot`, recursively — derived types included; another aggregate root is
never part of it. Mark a navigation to reference data `IsOutsideAggregate()`: it is
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

Uniqueness holds per row too: a navigation brings an entity into the boundary only if each of its rows
has one parent row — a collection or a reference whose child holds the foreign key, or a one-to-one
whose parent holds a unique foreign key. A many-to-one reference held by the parent that reaches a
non-root entity fails model building, naming the navigation: make the target an aggregate root, or mark
the navigation `IsOutsideAggregate()`.

A many-to-many (skip) navigation declared inside a boundary fails model building whatever its target,
unless it is marked `IsOutsideAggregate()` — then it is neither loaded nor versioned, and its join rows
lie outside every aggregate. Links an aggregate owns are an explicit link entity, a child of the root
that references the other root — the `TagLinks` of the `Order` above, keyed by `(OrderId, TagId)`:

```csharp
modelBuilder.Entity<TagLink>(link =>
{
    link.HasKey(l => new { l.OrderId, l.TagId });
    link.HasOne<Tag>().WithMany().HasForeignKey(l => l.TagId); // another aggregate: Restrict by convention
});
```

The links are loaded, versioned and removable like any child, and the `Tag` itself is not tracked:
it loads whole through its own repository, in the same context if needed.

The database never changes an aggregate on behalf of something outside it. A foreign key from inside
an aggregate to another root, or to an entity outside every aggregate — `TagLink.TagId` above, an
`Order.CustomerId` — gets `DeleteBehavior.Restrict`, instead of EF Core's `Cascade` default for a
required relationship ([cascade
delete](https://learn.microsoft.com/ef/core/saving/cascade-delete#configuring-cascading-behaviors)):
deleting a `Tag` that is still linked fails, and nothing changes. An explicit `Cascade` or `SetNull`
on such a key fails model building, naming it. Relationships inside one boundary keep their
behaviour: a child still cascades with its root.

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

## Optimistic concurrency

Every aggregate root carries a version: a shadow `long` concurrency token named
`AggregateVersion` (`AggregateVersion.PropertyName`), added by `UseMicroKitPersistence()`. Every
save that writes any entity of an aggregate — the root, a child, a grand-child, an owned type, a
link entity — bumps its root's version by one, so two commands that change different
children of the same aggregate conflict: the second save fails with a `PersistenceException`
wrapping `DbUpdateConcurrencyException`, and nothing of it is written. The check runs in the root's
`UPDATE` (`WHERE AggregateVersion = @original`).

- An inserted aggregate starts at 1; `GetVersion` reads 0 on an `Added` root until its commit.
- A child that moves from one aggregate to another bumps both, and both must have been loaded or
  staged by their repository; a changed child whose root is not tracked fails the save. Entities
  outside every aggregate (an outbox row, reference data) are ignored.
- The property name is reserved on aggregate roots. Its column may be configured
  (`Property<long>(AggregateVersion.PropertyName).HasColumnName("row_version")`), and a naming
  convention applies to it.

Across requests — an HTTP `ETag` / `If-Match` — read the version from a loaded aggregate or from a
projection, and state it on the command:

```csharp
public async ValueTask<long> HandleAsync(OrderId id, long ifMatch, string sku, Actor by, CancellationToken ct = default)
{
    var order = await orders.FindAsync(id, expectedVersion: ifMatch, ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Order {id} does not exist.");

    order.AddLine(sku, 1, by, clock);
    await unitOfWork.CommitAsync(ct).ConfigureAwait(false); // UPDATE … WHERE AggregateVersion = @ifMatch

    return orders.GetVersion(order); // the new ETag
}
```

```csharp
public static IQueryable<OrderSummary> Of(ShopDbContext context) =>
    context.Orders.AsNoTracking()
        .Select(o => new OrderSummary(o.Id, o.Number, EF.Property<long>(o, AggregateVersion.PropertyName)));
```

The expected version becomes the token's original value: the check runs in the `UPDATE` of the
commit, never as a read-then-compare. A command that states the current version but changes
nothing writes nothing. A command that states a stale version fails with a conflict even when it
changes nothing: EF Core sees the original value differ from the loaded one and writes the root's
token, `WHERE AggregateVersion = @stale`, which matches no row.

A staged aggregate not inserted yet has version 0: `FindAsync(id, expectedVersion)` checks it at the
find, and any other expected value throws. A delete is checked too: `DeleteAsync` then a commit fails
when another command changed the aggregate since it was loaded.

Known limits:

- Bulk writes bypass the version: `ExecuteUpdate`, `ExecuteDelete` and raw SQL do not go through the
  change tracker, so they write an aggregate's rows without bumping its version and without
  conflicting with a concurrent command.
- MicroKit bumps the versions in a `SaveChanges` interceptor that runs before the interceptors the
  application adds with `AddInterceptors`: a change those make to an aggregate during `SavingChanges`
  is written without a bump.
- A many-to-many navigation marked `IsOutsideAggregate()` is not versioned: its join rows lie outside
  every aggregate. Use a link entity for links the aggregate owns.

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
  the operation's exceptions through to the transaction's owner;
- an ambient `System.Transactions` transaction (`TransactionScope`) is rejected, as EF Core rejects it:
  with retry on, the execution strategy refuses to start; without, beginning the transaction fails. A
  distributed transaction is not a consistency mechanism between services: they go through the outbox.

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

## Persistence failures as `Result`

`IResultUnitOfWork` (core) returns three persistence failures as `MicroKit.Result` errors of category
`Conflict` instead of throwing; every other failure still throws `PersistenceException`:

| Failure | Recognised by | Code |
|---|---|---|
| Unique-constraint violation | PostgreSQL `23505`, SQL Server `2627`/`2601` | `PERSISTENCE.<ROOT>.DUPLICATE` |
| Foreign-key violation | PostgreSQL `23503`, SQL Server `547` (see below) | `PERSISTENCE.<ROOT>.REFERENCE` |
| Concurrency conflict | `DbUpdateConcurrencyException`, a stale expected version | `PERSISTENCE.<ROOT>.CONCURRENCY` |

```csharp
public sealed class PlaceOrder(
    IResultUnitOfWork transactions,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    // README: "Persistence failures as Result"
    public ValueTask<Result<OrderId>> HandleAsync(string number, Actor by, CancellationToken ct = default) =>
        transactions.TryExecuteAsync(
            static async (s, token) =>
            {
                if (string.IsNullOrWhiteSpace(s.Number))
                    return Result.Failure<OrderId>(Error.Validation(ErrorCode.From("ORDER.NUMBER.EMPTY"), "An order needs a number."));

                var order = Order.Place(s.Number, s.By, s.Clock);
                await s.Orders.AddAsync(order, token).ConfigureAwait(false);
                await s.UnitOfWork.CommitAsync(token).ConfigureAwait(false);
                return Result.Success(order.Id);
            },
            (Orders: orders, UnitOfWork: unitOfWork, Clock: clock, Number: number, By: by),
            ct);
}
```

A second `SO-1` returns `PERSISTENCE.ORDER.DUPLICATE` (409 through `MicroKit.Result.AspNetCore`), and
nothing of the command is written.

- **Where it converts.** `TryExecuteAsync` runs the operation as `ExecuteAsync` does — transaction,
  execution strategy, transient failures retried — and converts a failure only once the transaction it
  owns is rolled back and the strategy has given up. Converting inside the transaction would let the
  operation return normally, and the owner would commit what was written before it (an earlier flush,
  an `ExecuteUpdate`, outbox rows). Joined to an open transaction, it converts nothing: the exception
  reaches the owner, which rolls back.
- **A failed `Result` the operation returns** is returned unchanged, and the transaction it owns is
  rolled back instead of committed: a failed command persists none of its work. Joined, the owner
  decides.
- **`TryCommitAsync`** is the `Result` counterpart of `CommitAsync`, for code with no open transaction
  (a flush is atomic by itself); inside an open transaction it throws `InvalidOperationException`.
- **`<ROOT>`** is the failing entity's aggregate root, by default its simple CLR name in UPPER_SNAKE
  (`OrderLine` → `ORDER_LINE`); `Entity<Billing.Order>().HasAggregateErrorName("BILLING_ORDER")` fixes
  it, and model building fails when two roots resolve to the same name. When the failing entities belong
  to more than one aggregate, or to none (an outbox row, a constraint checked at COMMIT), the code is
  `PERSISTENCE.<REASON>`: a root is never guessed.
- **The message is fixed and client-safe**: no table, constraint or column name. The provider exception
  stays in process, in EF Core's log (`SaveChangesFailed`).
- **Provider codes** are recognised by `PostgreSqlFailureClassifier` and `SqlServerFailureClassifier`,
  which `UsePostgreSQL` and `UseSqlServer` configure. A context configured another way passes one:
  `UseMicroKitPersistence(PostgreSqlFailureClassifier.Instance)`; without one, only concurrency
  conflicts are converted.
- **SQL Server `547`** is reported for a foreign-key and for a check violation alike. It is decided from
  the model: `REFERENCE` only when no entity type mapped to the failing entries' tables declares a check
  constraint (`HasCheckConstraint`); otherwise it is not converted. A check constraint that exists in the
  database but not in the model cannot be seen, and its violation is reported as `REFERENCE`. A
  PostgreSQL check violation (`23514`) is never converted.
- A strategy that retries every exception, rather than the provider's transient ones, would also re-run
  an operation that returned a failed `Result`.
- `IResultUnitOfWork` writes nothing by itself: the operation flushes with `IUnitOfWork.CommitAsync`, so
  a handler injects both. After a converted `TryCommitAsync` failure the change set is still pending;
  call `DiscardChanges()` before reusing the scope.
- Inside a transaction someone else owns — MicroKit.MediatR's `TransactionBehavior` runs every command
  in `ExecuteAsync` — `TryExecuteAsync` is joined and converts nothing, and `TryCommitAsync` is refused:
  the conversion belongs to the transaction's owner. MicroKit.MediatR adopts it during its own upgrade.

The in-memory double follows the same transaction rules: a version conflict of `InMemoryRepository`
(checked at the find) becomes `PERSISTENCE.<ROOT>.CONCURRENCY` with the default name inside
`TryExecuteAsync`, and a failed operation counts no commit:

```csharp
var orders = new InMemoryRepository<Order, OrderId>();
var unitOfWork = new InMemoryUnitOfWork();
var order = Order.Place("SO-1", new Actor("user-1"), TimeProvider.System);
await orders.AddAsync(order);
orders.SetVersion(order, 2); // another command changed it

var result = await unitOfWork.TryExecuteAsync(
    static async (s, ct) =>
    {
        var stale = await s.Orders.FindAsync(s.Id, expectedVersion: 1, ct);
        stale!.AddLine("BOOK", 1, new Actor("user-1"), TimeProvider.System);
        await s.UnitOfWork.CommitAsync(ct);
        return Result.Success();
    },
    (Orders: orders, UnitOfWork: unitOfWork, order.Id));

// result.Error.Code == "PERSISTENCE.ORDER.CONCURRENCY", category Conflict; no commit counted.
```

The double cannot roll its store back: a failed operation's writes stay visible in it. It has no model,
so a name fixed with `HasAggregateErrorName` is unknown to it. It has no constraints either: it never
returns `DUPLICATE` or `REFERENCE` (adding an aggregate twice throws at `AddAsync`), and since it checks
versions at the find, a stale find outside `TryExecuteAsync` throws instead of reaching `TryCommitAsync`
as a `CONCURRENCY` error.

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
same pagination limits as the EF Core repository. Its versions start at 1 and do not move on their
own (it cannot see mutations): `SetVersion` sets one, and `FindAsync(id, expectedVersion)` checks it at
the find, where EF Core checks it at the commit.

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
| `IRepository<TAggregate, TId>` | Abstractions | Write side: whole-aggregate load (optionally at an expected version), add, delete, version |
| `IUnitOfWork` | Abstractions | Commit or discard the pending change set |
| `ITransactionalContext` | Abstractions | Run an operation in a transaction (retry, join) |
| `IPagedResult<T>` | Abstractions | A page of items with the total count |
| `PersistenceException` | Abstractions | Every database failure surfaced by the module |
| `IResultUnitOfWork` | Core | `TryExecuteAsync`, `TryCommitAsync`: conflicts as `Result` errors, converted after the rollback |
| `PersistenceErrors` | Core | The `PERSISTENCE.<ROOT>.<REASON>` errors, `DefaultErrorName`, `IsValidErrorName` |
| `AggregateVersionConflictException` | Core | Inner exception of a version conflict a repository detects itself |
| `IReadRepository<TAggregate>` | Core | Read side, untracked: find, list, page, any, count |
| `QueryOptions<TAggregate>` | Core | Specification + includes, split query, ordering, pagination |
| `QueryOptionsExtensions` | Core | `WithIncludes`, `WithPagination`, `AsSplitQuery`, `WithOrdering` |
| `PaginationOptions` | Core | A validated page request |
| `PaginationLimits` | Core | Default and maximum page sizes |
| `PagedResult<T>` | Core | `IPagedResult<T>` implementation |
| `ISpecificationEvaluator` | Core | Applies `QueryOptions` to an `IQueryable<T>` |
| `EfRepository<TAggregate, TId, TContext>` | EntityFrameworkCore | EF Core write-side base; `LoadAggregatesAsync` for typed finders |
| `EfReadRepository<TAggregate, TContext>` | EntityFrameworkCore | EF Core read-side base |
| `EfUnitOfWork<TContext>` | EntityFrameworkCore | `IUnitOfWork` + `ITransactionalContext` + `IResultUnitOfWork` over a `DbContext` |
| `ITransactionalUnitOfWork` | EntityFrameworkCore | The two contracts as one service |
| `EfSpecificationEvaluator` | EntityFrameworkCore | EF Core `ISpecificationEvaluator` |
| `EfDomainEventsProvider<TContext>` | EntityFrameworkCore | Drains the domain events of every tracked aggregate |
| `PersistenceServiceCollectionExtensions` | EntityFrameworkCore | `AddMicroKitPersistence`, `AddEntityFrameworkCore`, `AddDbContext`, `AddUnitOfWork`, `ConfigurePagination` |
| `PersistenceBuilder`, `EfCoreBuilder` | EntityFrameworkCore | Registration builders |
| `ProviderOptions`, `RetryOnFailureOptions` | EntityFrameworkCore | Opt-in retry and pooling |
| `DomainConventionExtensions` | EntityFrameworkCore | MicroKit.Domain mappings for `ConfigureConventions` |
| `MicroKitPersistenceDbContextOptionsExtensions` | EntityFrameworkCore | `UseMicroKitPersistence()`: boundary validation, the loaded-root record, the aggregate version and its `SaveChanges` step; `UseMicroKitPersistence(classifier)` adds the provider's error classifier |
| `IDatabaseFailureClassifier`, `DatabaseFailure` | EntityFrameworkCore | The seam a provider package recognises its error codes through |
| `AggregateErrorNameBuilderExtensions` | EntityFrameworkCore | `HasAggregateErrorName` on a root |
| `AggregateBoundaryNavigationBuilderExtensions` | EntityFrameworkCore | `IsOutsideAggregate()` on a navigation |
| `AggregateVersion` | EntityFrameworkCore | `PropertyName` of the version token, for projections |
| `PostgreSqlEfCoreBuilderExtensions` | PostgreSql | `UsePostgreSQL<TContext>` |
| `PostgreSqlFailureClassifier` | PostgreSql | `23505`, `23503` |
| `SqlServerEfCoreBuilderExtensions` | SqlServer | `UseSqlServer<TContext>` |
| `SqlServerFailureClassifier` | SqlServer | `2627`, `2601`, `547` |
| `QueryOptionsOrderingExtensions` | Specifications | `WithOrderBy`, `WithOrderByDescending`, `WithThenBy`, `WithThenByDescending` |
| `QueryOptionsSpecificationExtensions` | Specifications | `WithSpec` |
| `InMemoryRepository<TAggregate, TId>` | Testing | Write and read double; versions checked at the find (`SetVersion`) |
| `InMemoryReadRepository<TAggregate>` | Testing | Read-only double |
| `InMemoryUnitOfWork` | Testing | Counts commits and discards; `IResultUnitOfWork` double |
| `ReadRepositoryMutationAnalyzer`, `DirectSaveChangesAnalyzer`, `DbContextInjectionAnalyzer`, `RepositoryIQueryableLeakAnalyzer` | Analyzers | MKP001–MKP005 |

## Decisions

Architecture decisions live in
[`.claude-context/context/architectural-decisions.md`](.claude-context/context/architectural-decisions.md);
the breaking changes of each version in [`CHANGELOG.md`](CHANGELOG.md).
