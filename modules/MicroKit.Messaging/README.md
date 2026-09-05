# MicroKit.Messaging

Transactional outbox, idempotent inbox, and a broker-agnostic transport seam for .NET 10 — without coupling your domain to a broker.

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
- Broker-agnostic core — it defines `IMessageTransport` and ships no implementation; brokers are separate v2 packages
- Outbox claim is one atomic `UPDATE … WHERE` carrying an ownership token; a lost lease writes zero rows
- Retries use full jitter (`Uniform(0, min(2^n s, cap))`), so a broker outage does not resynchronise the queue
- One DI scope per message — a failure on message N cannot corrupt message N+1
- Tenant-aware: `TenantId` travels on the row, never on an ambient HTTP context

---

## Packages

| Package | Description |
|---------|-------------|
| `MicroKit.Messaging.Abstractions` | Contracts: `IIntegrationEvent`, `IIntegrationEventPublisher`, `IMessageHandler<T>`, `IOutboxWriter`, `IMessageTransport`, `MessageEnvelope`, the outbox/inbox stores, `OutboxMessage`, `InboxMessage`, `IntegrationEventWriteResult` |
| `MicroKit.Messaging` | Outbox/inbox processors and workers, the transport dispatcher, integration-event publishing, `OutboxMessageFactory`, DI |
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

// 4. Messaging. Domain events only — no transport, and none needed.
services.AddMicroKitMessaging()
    .AddEfCoreOutbox<AppDbContext>()
    .AddMediatRDomainEvents();    // only with MicroKit.Messaging.MediatR
```

> **PostgreSQL and SQLite only.** `ApplyMessagingConfiguration()` declares a unique index over
> `(OriginMessageId, ContractName)` on the outbox, and that index is a model invariant rather than
> an index you may skip. **SQL Server is not supported for it**: it compares nulls as equal in a
> unique index, and every notification row carries `(NULL, NULL)` — so the second notification row
> your application ever writes is rejected. Not at DDL time, not on the first row: in production, on
> the second insert. See *State* below for the workaround you would own.

**Nothing here composes in a required order.** Add `.AddTransportDispatcher()` (or a broker
provider's `Add{Provider}Transport()`, which calls it) when you publish integration events, before
or after `AddMediatRDomainEvents()` — either works.

A host that publishes only domain-event notifications registers no transport at all, as above. Do
**not** call `AddTransportDispatcher()` "just in case": it declares an intent to send contracts, and
without an `IMessageTransport` behind it the outbox stops on the first row of any kind — loudly,
with the batch released and nothing lost, but stopped.

> ⚠ **`AddMessageHandler<THandler, TEvent>()` fails at startup in this release.** Nothing produces
> inbox rows yet — the in-process fan-out was withdrawn and the receiving seam that turns a
> `MessageEnvelope` back into per-consumer rows has not shipped. The inbox drain itself is intact
> and correct; it simply has no producer, and a host is told so rather than left believing it
> consumes events. See ADR-MSG-019.

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
        .AddTransportDispatcher()   // plus an IMessageTransport from a broker provider
        .AddIntegrationEventPublishing()
        .AddEfCoreIntegrationEvents<AppDbContext>();
```

### Consuming a contract you do not publish

A module that *understands* a contract declares it separately, and the declaration looks almost the
same — the same attribute, carrying the same wire name, on **its own** local type. The producer's
type does not travel and would be useless if it did.

```csharp
// The shipping module's own type for a contract the orders module emits. Same name, different
// assembly, different CLR type — that is the whole point of a wire name.
[IntegrationEvent("shop.orders.order-placed.v1")]
public sealed record OrderPlaced(Guid OrderId, Guid CustomerId) : IIntegrationEvent;

services.AddIntegrationEventSubscriptions(events =>
{
    events.Consumes<OrderPlaced>();
});

services.AddMicroKitMessaging()
        .AddIntegrationEventConsumption();
```

`AddIntegrationEventSubscriptions` takes **no `source`**. A source names the module that *emitted*
an event; a consumer emitted nothing, so it has nothing truthful to put there.

`AddIntegrationEventConsumption()` wires the registry and its startup validator, and nothing else —
a service that only consumes gets the same boot-time check on duplicated contract names that a
publishing one gets, instead of discovering the collision inside a handler, inside a transaction,
where no retry can fix it. A service that does both calls both, in either order.

You only need `Consumes<T>()` for a contract this application does not publish itself: `Publishes<T>()`
already binds the name, so a modular monolith routes its own contracts with no second declaration.

Declaring both is accepted as a no-op — a module must not have to know whether its producer happens
to be in-process. What *is* rejected, at startup, is two different types claiming one contract name.

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

> ⚠ **`ExecuteAsync` is not decoration.** Publishing writes a row into the caller's transaction and
> never commits it — so without an open transaction the row would be committed on its own by the
> provider's implicit per-statement transaction: an event announced permanently, for a business fact
> the caller may still roll back. `IUnitOfWork.CommitAsync` **alone is not enough**: it is a bare
> `SaveChangesAsync` under that same implicit transaction, and never appears as an open one. The
> publisher refuses rather than accept a publication it cannot honour, and
> `IntegrationEventPublishException` says exactly this.

> **Why it writes rather than merely tracking the row.** The publication is guarded by a unique key
> on `(OriginMessageId, ContractName)`, and a unique key can only be consulted by attempting the
> insert. Doing that inside `PublishAsync` is what lets a replayed dispatch be absorbed silently —
> the handler is told nothing, and gets back the identifier of the row that already exists. Deferring
> the insert to the caller's commit would surface the collision one stack frame too late, where every
> notification handler would need a `try`/`catch` on a database exception to survive its own
> redelivery. One consequence worth knowing: the flush is not partial, so anything else you had
> pending on the same unit of work is written at that point too — inside your transaction, with the
> same rollback semantics as before.

Pass `occurredOnUtc` when you have it. The row keeps two timestamps — when the fact happened and
when it was staged — and they are not the same instant: staging happens one relay later, minutes
under load. Omitted, a consumer reads the relay's clock as the business time.

**What happens next.** After the commit the event is durable and has been sent nowhere, which is
the correct intermediate state: nothing is announced for a fact that did not happen, and the row
survives a crash. The outbox processor then claims it on a later pass — the same claim, lease,
back-off and dead-letter machinery the domain-event path uses — and hands it to `IMessageTransport`
as a `MessageEnvelope`. That second pass is why the outbox is called **reentrant**: one table, two
natures of row, the second produced by dispatching the first.

**A redelivered dispatch does not duplicate.** If the processor crashes between dispatching a
notification and recording it, the notification is redelivered and its handlers re-run — including
the one that publishes. The republication collides on `(OriginMessageId, ContractName)` and is
absorbed: `PublishAsync` returns the existing row's identifier and the handler never learns it
happened. This is bounded by retention, and the bound is enforced rather than assumed — a contract
row is never purged while the row that produced it can still be dispatched again.

---

## Sending to a broker

An outbox row carries one of two natures, declared in its `MessageKind` column. A `Contract` row is
handed to a transport; a `Notification` row is fanned out in process by `MicroKit.Messaging.MediatR`.
`AddTransportDispatcher()` wires the dispatcher for the first of those — and a provider's own
`Add{Provider}Transport()` calls it for you, so composing a broker is one line:

```csharp
services.AddMicroKitMessaging()
        .AddEfCoreOutbox<AppDbContext>()
        .AddRabbitMqTransport();      // wires the transport AND the dispatcher that feeds it
```

Call `AddTransportDispatcher()` yourself only when you register an `IMessageTransport` directly
rather than through a provider package. It is `TryAdd`, so calling it as well is a no-op rather than
a duplicate.

**No `IMessageTransport` implementation ships in MicroKit.** A provider package supplies one. That
is not an oversight: with no receiving seam built yet, an in-process transport could only either
return successfully for messages it never delivered — marking rows `Published` that are gone — or
exist purely to throw.

### What `SendAsync` returning means

> **The destination has acknowledged the message.** Not that it was enqueued for background
> delivery, not that it was buffered locally.

`OutboxProcessor` marks the row `Published` on that return, and `Published` is terminal. A transport
that hands off asynchronously makes the mark a lie in exactly the way an outbox exists to prevent.
Writing a transport? Return after the publisher confirm (RabbitMQ), after `SendMessagesAsync`
(Azure Service Bus), after the delivery report (Kafka) — and see the conformance obligation in
`IMessageTransport`'s docs.

Failures are classified by exception type, and the type is a statement about scope:

| Thrown | Meaning | Effect |
|---|---|---|
| `OutboxTransportUnavailableException` | the broker is unreachable, so the next message will fail too | batch released, **no retry consumed**, worker backs off |
| *anything untyped* | this one message failed while the transport is healthy | retried with jittered back-off |
| `OutboxPayloadException` | the destination rejects this content permanently | dead-lettered on first sight |

The first row is what stops an hour-long outage from dead-lettering the whole queue.

### With no transport registered

A `Contract` row then fails **loudly and reversibly**: the batch is released untouched, no retry
budget is consumed, the rows stay `Pending`, and the worker stops so the missing registration is
visible. Deploy the provider, restart, they drain.

This is not checked at startup, deliberately — whether a transport is needed depends on whether any
contract row exists, which is data rather than composition. An application that publishes only
domain-event notifications composes legitimately without one.

### One thing worth knowing before an incident

The transport dispatcher never deserializes the payload — it travels opaque, which is what lets a
consumer holding a different CLR type read it. The consequence is that a **corrupt payload is not
detected on the way out**. It travels, and dead-letters at the consumer, in the consumer's inbox.
That is the right place for it to fail, but it means a producer-side operator can see a perfectly
healthy queue while a consumer is dead-lettering.

---

## State

**Stable.** The outbox: atomic batch claim with an ownership token, buffered outcomes settled in one
write, full-jitter retry, dead-lettering, and the retention worker. **The inbox: the same atomic
claim, the dedup gate actually implemented, and success settled inside the handler's own
transaction.** The EF Core stores. The `MessageKind`-routed dispatch seam. The MicroKit.MediatR
glue and the domain-event → notification → outbox path.

**Not shipping yet.** No `IMessageTransport` implementation (broker providers are v2), and no
receiving seam — so nothing writes inbox rows in this release and `AddMessageHandler<,>()` fails at
startup rather than letting a host believe it consumes events. The inbox drain itself is complete
and correct; it has no producer. See ADR-MSG-019.

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
