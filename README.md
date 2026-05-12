# MicroKit

**Production-ready .NET 10 infrastructure library for SaaS platforms, modular monoliths, and distributed systems.**

[![Build](https://github.com/michaelatsey/microkit/actions/workflows/build.yml/badge.svg)](https://github.com/michaelatsey/microkit/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)

---

## Why MicroKit

Every serious .NET application needs the same infrastructure: a Result type, a command bus, reliable outbox messaging, idempotency enforcement, multi-tenancy, caching. The usual answer is to assemble these from separate libraries — MediatR for dispatch, Ardalis.Result for outcomes, MassTransit for messaging, a hand-rolled idempotency check.

The problem is not that those libraries are bad. The problem is that they do not compose. You wire MediatR behaviors yourself. You decide whether your idempotency check commits atomically with the business write, or not. You figure out whether your outbox envelope carries correlation metadata, or silently drops it. Each library solves one slice; you own the integration surface.

**MicroKit is the integration layer.** The packages are designed to work together: the idempotency store stages in the same EF Core `DbContext` as your aggregate so they commit in one round trip. The outbox wraps every message in an `EventEnvelope` before serialization so correlation IDs survive broker transit. The caching behavior reads `CacheOptions.BypassCache` from the query itself, not from a global flag. These are decisions that matter in production and that you cannot retrofit onto independently authored libraries.

**Why not MassTransit directly?** MassTransit is a transport framework. It is large, opinionated about its own saga and consumer model, and pulls in significant dependencies. MicroKit's outbox is a persistence primitive — it does not care whether you deliver messages to RabbitMQ, Azure Service Bus, or a MediatR `INotification`. You swap the transport, not your application code.

**Why not Ardalis.GuardClauses / Ardalis.Result?** Ardalis packages are self-contained and excellent. If all you need is a Result type, use one. MicroKit's Result exists because the rest of the suite depends on its exact invariants: `Error.None` as a typed sentinel, `Result.Failure` carrying a structured `Error` with code and message, and the guarantee that `Result<T>` cannot be constructed in an inconsistent state. A third-party Result type cannot provide that guarantee across the whole suite.

**What MicroKit is not:**

- Not a framework — it does not control your application startup, middleware order, or request lifecycle
- Not a full application stack — there is no scaffolding, no code generation, no conventions engine
- Not opinionated about your architecture — CQRS, vertical slice, clean, hexagonal: MicroKit works with all of them
- Not a replacement for your ORM, broker, or HTTP framework — it adapts to them

---

## What sets MicroKit apart

**Invariant-enforced Result type.** `Result<T>` throws at construction if `isSuccess && error != Error.None` or the inverse. You cannot accidentally construct a success result that carries an error, or a failure with `Error.None`. Invalid states are not a runtime surprise — they are a construction-time exception. This matters in production because silent invalid Result values produce misleading logs and incorrect branching.

**Entity identity guards.** `Entity<TKey>` uses `EqualityComparer<TKey>.Default.Equals(id, default!)` to reject `Guid.Empty`, `0`, and null at construction. A persisted aggregate with a default-value ID is a data integrity bug; MicroKit prevents it at the boundary, not downstream in a null check.

**Dual optimistic concurrency.** `AggregateRootBase` carries a domain-level `Version` int (incremented by domain methods) and a persistence-level `RowVersion` byte array (set by EF Core interceptor). `Version` tracks logical mutations; `RowVersion` provides the database CAS token. Two layers address two distinct failure modes — application-level conflict detection and database-level lost-update prevention.

**ISO 4217 currency precision.** `Money` reads decimal digit counts from .NET's `RegionInfo` and caches them in a `ConcurrentDictionary`. Rounding uses `MidpointRounding.AwayFromZero`. `ToSmallestUnit()` and `FromSmallestUnit()` convert for payment processor APIs. Most `Money` implementations hard-code two decimal places; MicroKit does not.

**4-state idempotency machine.** The idempotency behavior handles `Processing`, `Completed`, `Failed`, and `Cancelled` differently. A `Processing` record throws `IdempotencyProgressingException` (HTTP 409). A `Failed` or `Cancelled` record is deleted and the handler re-runs. A `Completed` record returns the cached response immediately. Most implementations check for Completed only; the concurrent-duplicate and retry-after-failure cases are left unhandled.

**EF Core idempotency store that never calls `SaveChangesAsync`.** `EfCoreIdempotencyStore` stages in the `DbContext` ChangeTracker only. The caller's `IUnitOfWork.SaveChangesAsync` commits the idempotency record and the business aggregate update in a single round trip — either both commit or both roll back. This is the correct design; a separate `SaveChangesAsync` in the store breaks atomicity.

**3-phase optimistic inbox locking.** `OptimisticInboxLockingStrategy` runs a read, a bulk `ExecuteUpdateAsync` with a re-check predicate, then a targeted re-fetch. No `SELECT FOR UPDATE`, no serializable isolation, no advisory locks. Multiple workers compete safely.

**Per-consumer inbox state.** `InboxMessage` stores the raw payload once. `InboxState` is a separate row per (message, consumer) pair. Ten handler types can process the same message at different rates and retry counts without interfering.

**Envelope wrapping before serialization.** `OutboxService.EnqueueAsync<T>` wraps the payload in `EventEnvelope<T>` before serializing. The envelope carries `TenantId`, `CorrelationId`, `CausationId`, `IdempotencyKey`, and `Metadata`. Correlation context survives broker transit even if the broker strips headers.

**Destination validation at enqueue time.** `OutboxService.ValidateMessage` throws `ArgumentException` if no destination is enabled, or if `PublishToBroker = true` but `BrokerTopic` is empty. Misconfiguration fails at the call site, not hours later in a background worker log.

**Write-once tenant context.** `TenantContext.SetTenant` throws `InvalidOperationException` on a second call within the same scope. The middleware sets the tenant once. All downstream code gets the same immutable tenant — there is no risk of mid-request overwrite.

**Startup-time DI validation.** `MultiTenantValidationService` runs `IModuleValidator.Validate()` during `IHostedService.StartAsync`. Missing `ITenantRegistry` fails startup with a named `InvalidOperationException`, not a `NullReferenceException` on the first request.

**Per-call cache expiration control.** `CacheOptions` carries `SlidingExpiration` and `Duration` per call. `BypassCache = true` skips both read and write for that call without changing any global configuration. Most distributed cache wrappers force a global expiration policy.

---

## Package overview

### Domain Primitives

| Package | Description |
|---|---|
| `MicroKit.Domain.Abstractions` | `Entity<TKey>`, `AggregateRootBase`, `ValueObject`, `Enumeration`, `IDomainEvent`, audit interfaces |
| `MicroKit.Domain` | `Result<T>`, `Error`, `Money`, `AuditedEntity`, `AuditedAggregateRoot` |

### CQRS and Dispatching

| Package | Description |
|---|---|
| `MicroKit.Cqrs.Abstractions` | `ICommand`, `IQuery<T>`, `ICommandBus`, `IQueryBus`, `ICommandHandler`, `IQueryHandler`, cache contracts |
| `MicroKit.Cqrs.MediatR` | `MediatRCommandBus`, `MediatRQueryBus` — MediatR as a swappable implementation |
| `MicroKit.Cqrs.MediatR.Behaviors` | `LoggingBehavior`, `ValidationBehavior` (FluentValidation, fail-fast), `PerformanceBehavior` |
| `MicroKit.Cqrs.MediatR.Caching` | `CachingBehavior`, `CacheInvalidationBehavior` — pipeline-level cache get/set/invalidate |

### Eventing

| Package | Description |
|---|---|
| `MicroKit.Events.Abstractions` | `IDomainEvent`, `IIntegrationEvent`, `IEvent` — boundary-separated event contracts |
| `MicroKit.Events` | `EventBase` — 6-field tracing contract, `MessageType` computed from `GetType().FullName!` |

### Messaging — Outbox/Inbox

| Package | Description |
|---|---|
| `MicroKit.Messaging.Abstractions` | `IOutboxService`, `IInboxHandler<T>`, `OutboxMessage`, `InboxMessage`, `InboxState` |
| `MicroKit.Messaging.Core` | `OutboxPublisherWorker`, `InboxPublisherWorker`, cleanup workers, `MessagingValidationService` |
| `MicroKit.Messaging.Persistence.EFCore` | `EfOutboxRepository`, `EfInboxMessageRepository`, 3-phase `OptimisticInboxLockingStrategy` |
| `MicroKit.Messaging.Transport.RabbitMQ` | RabbitMQ `IMessageTransport` implementation |

### Idempotency

| Package | Description |
|---|---|
| `MicroKit.Idempotency.Abstractions` | `IIdempotentRequest<T>`, `IIdempotencyStore`, `IRequestHasher`, `IdempotencyState` |
| `MicroKit.Idempotency.Core` | `IdempotencyProvider`, SHA-256 hasher, `IdempotencyCleanupWorker` |
| `MicroKit.Idempotency.EFCore` | EF Core store — stages in ChangeTracker, never calls `SaveChangesAsync` |
| `MicroKit.Idempotency.Redis` | Redis-backed idempotency store |
| `MicroKit.Idempotency.MediatR` | `IdempotencyBehavior` — 4-state machine as MediatR `IPipelineBehavior` |

### Data Access

| Package | Description |
|---|---|
| `MicroKit.Data.Abstractions` | `IRepository<T>`, `IReadRepository<T>`, `IUnitOfWork`, `ITransactionalContext` |
| `MicroKit.Data.EntityFrameworkCore` | `EfUnitOfWork<TDbContext>` — structured error logging, re-throws unchanged |
| `MicroKit.EntityFrameworkCore` | JSON value converters, EF Core model builder extensions |

### Multi-Tenancy

| Package | Description |
|---|---|
| `MicroKit.MultiTenancy.Abstractions` | `ITenantContext`, `ITenant`, `ITenantResolutionStrategy`, `IHasMultiTenant` |
| `MicroKit.MultiTenancy` | `TenantContext` (write-once), `CompositeTenantRegionResolver`, startup validator |
| `MicroKit.MultiTenancy.Extensions` | `TenantResolutionMiddleware`, `HeaderResolutionStrategy`, `JwtClaimResolutionStrategy` |
| `MicroKit.MultiTenancy.EFCoreStore` | `EFCoreTenantStore` — database-backed tenant lookup |
| `MicroKit.MultiTenancy.Redis` | `RedisTenantCache` — distributed tenant cache |

### Caching

| Package | Description |
|---|---|
| `MicroKit.Caching.Abstractions` | `ICacheService`, `CacheOptions` — zero dependencies |
| `MicroKit.Caching.Distributed` | `DistributedCacheService` — `IDistributedCache`-backed with configurable JSON serialization |
| `MicroKit.Caching.Distributed.Autofac` | Autofac registration extension |

---

## Dependency architecture

```
MicroKit.Abstractions          (shared markers, base interfaces)
         |
         +-- MicroKit.Domain.Abstractions
         |        |
         |        +-- MicroKit.Domain
         |
         +-- MicroKit.Cqrs.Abstractions
         |        |
         |        +-- MicroKit.Cqrs.MediatR
         |        +-- MicroKit.Cqrs.MediatR.Behaviors
         |        +-- MicroKit.Cqrs.MediatR.Caching
         |
         +-- MicroKit.Events.Abstractions
         |        |
         |        +-- MicroKit.Events
         |
         +-- MicroKit.Messaging.Abstractions
         |        |
         |        +-- MicroKit.Messaging.Core
         |                 |
         |                 +-- MicroKit.Messaging.Persistence.EFCore
         |                 +-- MicroKit.Messaging.Transport.RabbitMQ
         |
         +-- MicroKit.Idempotency.Abstractions
         |        |
         |        +-- MicroKit.Idempotency.Core
         |                 |
         |                 +-- MicroKit.Idempotency.EFCore
         |                 +-- MicroKit.Idempotency.Redis
         |                 +-- MicroKit.Idempotency.MediatR
         |
         +-- MicroKit.Data.Abstractions
         |        |
         |        +-- MicroKit.Data.EntityFrameworkCore
         |        +-- MicroKit.EntityFrameworkCore
         |
         +-- MicroKit.MultiTenancy.Abstractions
         |        |
         |        +-- MicroKit.MultiTenancy
         |                 |
         |                 +-- MicroKit.MultiTenancy.Extensions
         |                 +-- MicroKit.MultiTenancy.EFCoreStore
         |                 +-- MicroKit.MultiTenancy.Redis
         |
         +-- MicroKit.Caching.Abstractions
                  |
                  +-- MicroKit.Caching.Distributed
                           |
                           +-- MicroKit.Caching.Distributed.Autofac
```

No circular dependencies between packages. Abstractions packages carry zero third-party NuGet references.

---

## Getting started

The following example wires a domain aggregate, a command handler that enqueues an outbox message, a minimal API endpoint, and the DI registration.

### 1. Define the aggregate

```csharp
using MicroKit.Domain.Abstractions;
using MicroKit.Events;

public sealed class Order : AggregateRoot<Guid>
{
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }

    private Order() { }

    public static Order Place(Guid id, Guid customerId)
    {
        var order = new Order { Id = id, CustomerId = customerId, Status = OrderStatus.Pending };
        order.AddDomainEvent(new OrderPlacedEvent(id, customerId));
        return order;
    }

    public void Confirm()
    {
        Status = OrderStatus.Confirmed;
        AddDomainEvent(new OrderConfirmedEvent(Id, CustomerId));
    }
}
```

### 2. Define the command and handler

```csharp
using MicroKit.Cqrs.Abstractions.Commands;
using MicroKit.Data.Abstractions;
using MicroKit.Domain.Primitives;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.MultiTenancy.Abstractions;

public record ConfirmOrderCommand(Guid OrderId, string CorrelationId) : ICommand<Result>;

public sealed class ConfirmOrderHandler : ICommandHandler<ConfirmOrderCommand, Result>
{
    private readonly IRepository<Order> _orders;
    private readonly IUnitOfWork _uow;
    private readonly IOutboxService _outbox;
    private readonly ITenantContext _tenant;

    public ConfirmOrderHandler(
        IRepository<Order> orders,
        IUnitOfWork uow,
        IOutboxService outbox,
        ITenantContext tenant)
        => (_orders, _uow, _outbox, _tenant) = (orders, uow, outbox, tenant);

    public async Task<Result> HandleAsync(ConfirmOrderCommand cmd, CancellationToken ct)
    {
        var order = await _orders.FindByIdAsync(cmd.OrderId, ct);
        if (order is null) return Result.Failure(OrderErrors.NotFound);

        order.Confirm();
        _orders.Update(order);

        // Stages the outbox row in the same DbContext ChangeTracker.
        // SaveChangesAsync below commits the order update and the outbox row atomically.
        await _outbox.EnqueueAsync(
            tenantId: _tenant.Tenant!.Id,
            messageId: Guid.NewGuid().ToString(),
            payload: new OrderConfirmedIntegrationEvent(order.Id, order.CustomerId),
            destination: new OutboxDestination
            {
                PublishToBroker = true,
                BrokerTopic = "orders.confirmed",
                PartitionKey = order.CustomerId
            },
            correlationId: cmd.CorrelationId,
            cancellationToken: ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

### 3. Expose a minimal API endpoint

```csharp
app.MapPost("/orders/{id:guid}/confirm", async (
    Guid id,
    HttpContext http,
    ICommandBus bus,
    CancellationToken ct) =>
{
    var correlationId = http.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString();

    var result = await bus.SendAsync<Result>(
        new ConfirmOrderCommand(id, correlationId), ct);

    return result.IsSuccess
        ? Results.NoContent()
        : Results.UnprocessableEntity(result.Error.Message);
});
```

### 4. DI registration

```csharp
// Program.cs
builder.Services
    .AddMicroKit()
    .AddMicroKitMultiTenancy(options => options.HeaderName = "X-Tenant-Id")
    .WithHeaderStrategy()
    .AddMicroKitMessaging(messaging =>
    {
        messaging.UseOutbox(outbox =>
        {
            outbox.BatchSize       = 100;
            outbox.PollingInterval = TimeSpan.FromSeconds(5);
            outbox.MaxRetryCount   = 5;
        });
        messaging.AddRepositories<
            EfOutboxRepository<AppDbContext>,
            EfInboxMessageRepository<AppDbContext>>();
    });

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork<AppDbContext>>();
builder.Services.AddScoped<IRepository<Order>, OrderRepository>();
builder.Services.AddScoped<IReadRepository<Order>, OrderRepository>();

var app = builder.Build();
app.UseMicroKitMultiTenancy();
app.Run();
```

---

## Module documentation

| Module | Readme |
|---|---|
| `MicroKit.Domain` | [docs/Readme.md](MicroKit.Domain/docs/Readme.md) |
| `MicroKit.Cqrs` | [docs/Readme.md](MicroKit.Cqrs/docs/Readme.md) |
| `MicroKit.Events` | [docs/Readme.md](MicroKit.Events/docs/Readme.md) |
| `MicroKit.Messaging` | [docs/Readme.md](MicroKit.Messaging/docs/Readme.md) |
| `MicroKit.Idempotency` | [docs/Readme.md](MicroKit.Idempotency/docs/Readme.md) |
| `MicroKit.Data` | [docs/Readme.md](MicroKit.Data/docs/Readme.md) |
| `MicroKit.MultiTenancy` | [docs/Readme.md](MicroKit.MultiTenancy/docs/Readme.md) |
| `MicroKit.Caching` | [docs/Readme.md](MicroKit.Caching/docs/Readme.md) |

---

## Contributing

1. Fork the repository and create a feature branch from `main`.
2. Follow the engineering rules in [CLAUDE.md](CLAUDE.md) — these govern package design, dependency direction, API surface rules, and code quality requirements.
3. Write tests. Unit tests for domain and handler logic; integration tests via Testcontainers for anything that touches a database, Redis, or a broker. All existing tests must pass (`dotnet test`).
4. Open a pull request against `main`. Describe what the change does and why. Reference an issue if one exists.
5. Breaking public API changes require a major version bump and must be discussed in an issue before a PR is opened.

Report bugs and request features via [GitHub Issues](https://github.com/michaelatsey/microkit/issues).

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

---

## License

MIT — see [LICENSE](LICENSE).

Author: Ange Michael Atsé
