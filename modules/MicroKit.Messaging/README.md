# MicroKit.Messaging

Transactional outbox, idempotent inbox, and in-process transport for .NET 10 — without coupling your domain to a broker.

---

## Overview

Your code publishes a fact. This module guarantees it is delivered at least once, even if the process
dies between the database commit and the broker call — the problem a plain `publish()` after
`SaveChanges()` cannot solve.

```
command handler ──► outbox row  ┐
                                ├─ ONE transaction, ONE SaveChanges
aggregate changes ──────────────┘
                                        ▼ (after commit, background)
                            OutboxProcessor ──► handlers / broker
```

The row and the aggregate commit together or not at all. Nothing is published until the business
transaction is durable, and nothing durable goes unpublished.

**Key design points:**
- Broker-agnostic core — the v1 transport is in-process; brokers are separate v2 packages
- Outbox claim is one atomic `UPDATE … WHERE` carrying an ownership token; a lost lease writes zero rows
- Retries use full jitter (`Uniform(0, min(2^n s, cap))`), so a broker outage does not resynchronise the queue
- One DI scope per message — a failure on message N cannot corrupt message N+1
- Tenant-aware: `TenantId` travels on the row, never on an ambient HTTP context

---

## Packages

| Package | Description |
|---------|-------------|
| `MicroKit.Messaging.Abstractions` | Contracts: `IIntegrationEvent`, `IMessagePublisher`, `IMessageHandler<T>`, `IOutboxWriter`, the outbox/inbox stores, `OutboxMessage`, `InboxMessage` |
| `MicroKit.Messaging` | Outbox/inbox processors and workers, in-process transport, `OutboxMessageFactory`, DI |
| `MicroKit.Messaging.EntityFrameworkCore` | `EfOutboxStore`, `EfInboxStore`, entity configuration for your `DbContext` |
| `MicroKit.Messaging.MediatR` | Glue: puts MicroKit.MediatR domain events on the outbox as notifications |

---

## Installation

```bash
dotnet add package MicroKit.Messaging
dotnet add package MicroKit.Messaging.EntityFrameworkCore
dotnet add package MicroKit.Messaging.MediatR   # only if you use MicroKit.MediatR
```

---

## Composition

The full wiring, as it actually works. Call order does not matter except where noted.

```csharp
// 1. Your DbContext owns the outbox and inbox tables — that is what makes the write atomic.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyMessagingConfiguration();
}

// 2. Persistence: AddUnitOfWork binds IUnitOfWork / ITransactionalContext and registers the
//    IDomainEventsProvider the dispatcher drains — all on the same DbContext.
services.AddMicroKitPersistence(p => p
    .AddEntityFrameworkCore()
    .AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString))
    .AddUnitOfWork<AppDbContext>());

// 3. CQRS. TransactionBehavior (order 700) is the dispatch-and-commit owner: it dispatches domain
//    events and only then commits, so outbox rows land in the same SaveChanges as the aggregate.
services.AddMicroKitMediatR(cfg => cfg
    .FromAssemblyContaining<CreateOrderCommand>()
    .AddTransactionBehavior());

// 4. Messaging.
services.AddMicroKitMessaging()
    .AddEfCoreOutbox<AppDbContext>()
    .AddInProcessTransport()      // must precede the line below — it registers what that decorates
    .AddMediatRDomainEvents();    // only with MicroKit.Messaging.MediatR
```

`AddInProcessTransport()` before `AddMediatRDomainEvents()` is the one ordering requirement, and
getting it wrong throws at startup naming the fix. Everything else composes in any order.

A host built with `Host.CreateApplicationBuilder()` or `WebApplication.CreateBuilder()` already has
logging. A bare `ServiceCollection` does not, and several types here require `ILogger<T>` — call
`AddLogging()` yourself if you compose by hand.

> **Notification handlers must be idempotent.** There is no per-consumer inbox on the notification
> path: an outbox retry re-publishes the notification and re-runs *all* of its handlers. The same
> contract applies to `IMessageHandler<T>` on the inbox path — delivery is at-least-once by design.

---

## End to end

```csharp
// The fact.
public sealed record OrderPlacedEvent(Guid OrderId) : DomainEvent;

// The outbox payload. Declaring it is what puts the event on the outbox; an event with no
// notification is dispatched to its handlers and goes no further.
public sealed class OrderPlacedNotification(OrderPlacedEvent domainEvent)
    : DomainEventNotification<OrderPlacedEvent>(domainEvent);

// The command raises the event and stages the aggregate. It does not save, and does not publish.
public sealed class PlaceOrderHandler(IOrderRepository repo)
    : ICommandHandler<PlaceOrderCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(PlaceOrderCommand command, CancellationToken ct = default)
    {
        var order = Order.Place(command.CustomerId, command.Lines);
        order.Raise(new OrderPlacedEvent(order.Id));
        await repo.AddAsync(order, ct);
        return Result.Success(order.Id);
    }
}

// Runs after commit, from the outbox processor. Must be idempotent — it may run more than once.
public sealed class ProjectOrderHandler(IReadModel readModel)
    : INotificationHandler<OrderPlacedNotification>
{
    public Task Handle(OrderPlacedNotification n, CancellationToken ct)
        => readModel.UpsertOrderAsync(n.DomainEvent.OrderId, ct);
}
```

Sending the command writes the aggregate and one outbox row in a single transaction. `OutboxWorker`
picks the row up on its next poll and publishes it; `ProjectOrderHandler` runs then, not before.

---

## State

**Stable.** The outbox: atomic batch claim with an ownership token, buffered outcomes settled in one
write, full-jitter retry, dead-lettering, and the retention worker. The EF Core stores. The
in-process transport. The MicroKit.MediatR glue and the domain-event → notification → outbox path.

**Still moving.**
- **The inbox is being rewritten next.** It still uses the per-message lease and the deterministic
  back-off the outbox has left behind, its options callback is a no-op (`InboxProcessorOptions` has
  `init`-only accessors), and the dedup gate documented on `IInboxStore` is not implemented — a
  redelivered message currently fails its duplicate insert and burns retries. Treat the inbox as
  preview.
- **Broker providers** (RabbitMQ, Azure Service Bus, Kafka) are scaffolded but unimplemented — v2.
- **Schema ownership.** There is no published SQL for the outbox/inbox tables; the only definition is
  the EF Core entity configuration. If you own your own DDL, you are reverse-engineering it.
- Domain events raised by a notification handler are staged but never flushed, so cascade events are
  currently lost. Tracked; do not rely on cascade dispatch.

All packages are `1.0.0-preview.*`. Contracts may still change between previews.
