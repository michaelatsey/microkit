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
| `MicroKit.Messaging.Abstractions` | Contracts: `IIntegrationEvent`, `IIntegrationEventPublisher`, `IMessageHandler<T>`, `IOutboxWriter`, the outbox/inbox stores, `OutboxMessage`, `InboxMessage`, `IntegrationEventMessage` |
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

> **PostgreSQL and SQLite only.** `ApplyMessagingConfiguration()` declares a unique index over
> `(OriginMessageId, ContractName)` on the outbox, and that index is a model invariant rather than
> an index you may skip. **SQL Server is not supported for it**: it compares nulls as equal in a
> unique index, and every notification row carries `(NULL, NULL)` — so the second notification row
> your application ever writes is rejected. Not at DDL time, not on the first row: in production, on
> the second insert. See *State* below for the workaround you would own.

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

## Publishing an integration event

A domain event is internal. An **integration event** is the contract you publish for other bounded
contexts, and it is a different type on purpose — an aggregate should never reference one.

```csharp
// Business payload only. Everything about delivery — identity, tenant, correlation, timestamps —
// is assigned when the row is staged. The attribute is the wire name and is mandatory: a CLR type
// name cannot serve as one, because a consumer in another service holds a different type.
[IntegrationEvent("shop.orders.order-placed.v1")]
public sealed record OrderPlaced(Guid OrderId, Guid CustomerId) : IIntegrationEvent;
```

Composition — contracts once per module, publishing once per application:

```csharp
services.AddIntegrationEventContracts("/shop/orders", events =>
{
    events.Publishes<OrderPlaced>();
});

services.AddMicroKitMessaging()
        .AddEfCoreOutbox<AppDbContext>()
        .AddInProcessTransport()
        .AddIntegrationEventPublishing()
        .AddEfCoreIntegrationEvents<AppDbContext>();
```

The call site is a notification handler — the point where a domain fact becomes a published
contract:

```csharp
public sealed class PublishOrderPlacedHandler(
    IIntegrationEventPublisher publisher,
    ITransactionalContext transaction,
    IUnitOfWork unitOfWork)
    : INotificationHandler<OrderPlacedNotification>
{
    public Task Handle(OrderPlacedNotification n, CancellationToken ct) =>
        transaction.ExecuteAsync(
            static async (state, token) =>
            {
                await state.Publisher.PublishAsync(
                    new OrderPlaced(state.OrderId, state.CustomerId),
                    occurredOnUtc: state.OccurredAt,
                    token);

                await state.UnitOfWork.CommitAsync(token);
            },
            (Publisher: publisher,
             UnitOfWork: unitOfWork,
             n.DomainEvent.OrderId,
             CustomerId: n.DomainEvent.CustomerId,
             OccurredAt: n.DomainEvent.OccurredAt),
            ct);
}
```

> ⚠ **`ExecuteAsync` is not decoration.** Publishing stages a row into the caller's transaction and
> never commits, so without an open transaction the row goes to a change tracker nobody saves and
> the event silently never existed. `IUnitOfWork.CommitAsync` **alone is not enough**: it is a bare
> `SaveChangesAsync` under the provider's implicit per-call transaction, which never appears as an
> open one. The publisher refuses rather than accept a publication it cannot honour, and
> `IntegrationEventPublishException` says exactly this.

Pass `occurredOnUtc` when you have it. The row keeps two timestamps — when the fact happened and
when it was staged — and they are not the same instant: staging happens one relay later, minutes
under load. Omitted, a consumer reads the relay's clock as the business time.

**Nothing delivers these rows yet.** After the commit the event is durable and has been sent
nowhere, which is the correct intermediate state: nothing is announced for a fact that did not
happen, and the row survives a crash. The relay and the broker adapters are the transport work.

---

## State

**Stable.** The outbox: atomic batch claim with an ownership token, buffered outcomes settled in one
write, full-jitter retry, dead-lettering, and the retention worker. **The inbox: the same atomic
claim, the dedup gate actually implemented, and success settled inside the handler's own
transaction.** The EF Core stores. The in-process transport. The MicroKit.MediatR glue and the
domain-event → notification → outbox path.

### What the inbox guarantees

**Transactionally atomic processing for database-backed handlers.** The processed mark and any
database side effects your handler writes through the execution scope's `DbContext` commit together
or not at all — the mark is staged into your unit of work, not written after it.

It is **not** exactly-once in general. A handler that calls an external endpoint and then rolls back
calls it again on replay: database effects happen effectively once, external effects at least once.
A handler that commits no unit of work at all is **detected and reported**, not degraded silently.

`LeaseDuration` is the setting that matters: it must comfortably exceed your worst-case handler
duration. `InboxBatchResult.LeasesLost` is the signal that it does not — without that counter, a
lease set too short is invisible, and the system stays correct while quietly doing less work than it
appears to.

**Inbox retention is not housekeeping.** `RetentionDays` defaults to 30, deliberately unlike the
outbox's 7: the table only deduplicates messages it still holds, so the window must exceed the
maximum plausible redelivery delay of every upstream transport. Deleting too eagerly reopens the
door to reprocessing.

**Still moving.**
- **Broker providers** (RabbitMQ, Azure Service Bus, Kafka) are scaffolded but unimplemented — v2.
- **Schema ownership.** There is still no published canonical SQL for the outbox and inbox tables;
  the only full definition is the EF Core entity configuration, so a consumer who owns their own DDL
  is reverse-engineering it. The CHANGELOG publishes the *migration* for the inbox claim rewrite
  (including the primary-key move, which is not optional) and for the outbox message-kind columns —
  that is a step, not the fix.
- **Provider support for the outbox replay key.** `UX_OutboxMessages_Origin_ContractName`, unique
  over `(OriginMessageId, ContractName)`, is what stops a redelivered dispatch from writing a
  duplicate integration message. It is a **model invariant, not an index you may skip**, and it is
  supported on **PostgreSQL and SQLite**, where nulls are distinct in a unique index — every
  notification row carries `(NULL, NULL)`, so unlimited such rows must coexist. **SQL Server is not
  supported for this constraint**: it compares nulls as equal, so the second notification row ever
  written is rejected — not at DDL time and not on the first row, but on the second insert in
  production. The CHANGELOG carries the filtered-index workaround for anyone who must run there
  anyway; it is a workaround you own, not a supported configuration.
- Domain events raised by a notification handler are staged but never flushed, so cascade events are
  currently lost. Tracked; do not rely on cascade dispatch.

All packages are `1.0.0-preview.*`. Contracts may still change between previews.
