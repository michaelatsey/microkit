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
| `MicroKit.Messaging.Abstractions` | Contracts, in two layers. **`MicroKit.Messaging`** — what an application names: `IIntegrationEvent`, `MessageContractAttribute`, `IMessageBus`, `IIntegrationEventPublisher`, `IMessageHandler<T>`, `MessageId`, `IntegrationEventPublishException`, `IntegrationEventConfigurationException`, `InboxDependencyUnavailableException`. **`MicroKit.Messaging.Spi`** — what an author of a store, transport or dispatcher implements: `IMessageTransport`, `MessageEnvelope`, `IEnvelopeReceiver`, `IOutboxDispatcher`, `IOutboxWriter` and the other store interfaces, `OutboxMessage`, `InboxMessage`, `IntegrationEventWriteResult` |
| `MicroKit.Messaging` | The bus and its in-memory transport, the outbox/inbox processors and workers, the transport dispatcher, integration-event publishing, `AddMicroKitMessaging` |
| `MicroKit.Messaging.EntityFrameworkCore` | `EfOutboxStore`, `EfInboxStore`, entity configuration for your `DbContext` |
| `MicroKit.Messaging.MediatR` | Glue: puts MicroKit.MediatR domain events on the outbox as notifications |

---

## Installation

```bash
dotnet add package MicroKit.Messaging                      # the bus, the in-memory transport, the outbox and inbox engine
dotnet add package MicroKit.Messaging.EntityFrameworkCore  # only for the outbox and inbox stores
dotnet add package MicroKit.Messaging.MediatR              # only to put MicroKit.MediatR domain events on the outbox
```

The bus needs nothing else: no database, no broker. Persistence is a feature you add, not a
prerequisite.

---

## Composition

One entry point, `AddMicroKitMessaging(bus => ...)`, validated when the host starts.

### Quick start — publish and consume, no database

A message type carries its wire contract; a handler is its subscription:

<!-- snippet: quick-start-contracts -->
```csharp
[MessageContract("shop.orders.order-placed")]   // wire type: shop.orders.order-placed.v1
public sealed record OrderPlaced(Guid OrderId) : IIntegrationEvent;

public sealed class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) : IMessageHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        logger.LogInformation("Order {OrderId} placed ({MessageId})", message.OrderId, context.MessageId);
        return ValueTask.CompletedTask;
    }
}
```

The whole composition:

<!-- snippet: quick-start -->
```csharp
services.AddMicroKitMessaging(bus =>
{
    bus.ServiceName = "shop.orders";
    bus.Publishes<OrderPlaced>();
    bus.AddHandler<OrderPlacedHandler>();
    bus.UseInMemoryTransport();
});
```

Publish from any scoped service:

<!-- snippet: quick-start-publish -->
```csharp
public sealed class Checkout(IMessageBus bus)
{
    public async Task PlaceAsync(Guid orderId, CancellationToken ct = default)
        => await bus.PublishAsync(new OrderPlaced(orderId), ct: ct);
}
```

- **`bus.ServiceName`** names the service. It is the `Source` of every message the service
  publishes, and the endpoint the service consumes from.
- **`bus.Publishes<T>()`** declares a publication. Publications are declared rather than
  discovered: that is what lets startup detect a duplicated contract name and a publication with
  no route.
- **`bus.AddHandler<THandler>()`** registers one subscription per closed `IMessageHandler<T>` the
  handler implements, so a handler of two message types is registered once. The contract comes from
  each type's `[MessageContract]`, with no separate declaration. Registering a handler twice is a
  no-op.
- **`bus.UseInMemoryTransport()`** registers the in-memory transport and its consume loop, and the
  dispatcher that routes outbox contract rows to it.

### A service consuming another service's contract

The consumer declares the producer's contract name on **its own** local type. The producer's type
does not travel, and would be useless if it did:

<!-- snippet: consuming-contract -->
```csharp
[MessageContract("shop.orders.order-placed")]   // the producer's name, on this service's own type
public sealed record OrderPlaced(Guid OrderId) : IIntegrationEvent;

public sealed class ReserveStock : IMessageHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
```

<!-- snippet: consuming-composition -->
```csharp
services.AddMicroKitMessaging(bus =>
{
    bus.ServiceName = "shop.shipping";
    bus.AddHandler<Shipping.ReserveStock>();   // a handler is its subscription
    bus.UseInMemoryTransport();
});
```

Two services on one `InMemoryBroker` each receive their own copy of a message. Two hosts with the
same `ServiceName` share one endpoint and compete for its messages, so each message is handled once
between them. A host binds its own endpoint on the first use of its transport or when it starts,
whichever comes first, so a message a single host publishes before it starts still reaches its own
handlers. Across hosts sharing one broker — tests only — start the consumers before the producers:
a message sent while no endpoint is bound to its type reaches no one (`Warning` 3014). Each host
sets its own `bus.ServiceName`: left unset, they all take the entry assembly's name and share one
endpoint. See `InMemoryBroker` for routing, redelivery and dead letters, which are kept per
endpoint.

### Handler discovery

`AddHandler<THandler>()` is the trim-safe registration. To register every handler of an assembly:

<!-- snippet: handler-discovery -->
```csharp
services.AddMicroKitMessaging(bus =>
{
    bus.ServiceName = "shop.shipping";
    bus.AddHandlersFromAssemblyContaining<OrderPlacedHandler>();   // reflection: not trim-safe
    bus.UseInMemoryTransport();
});
```

> ⚠ **Not trim-safe.** The scan reads the assembly's types by reflection, so a handler the trimmer
> removed is silently not registered. The method carries `[RequiresUnreferencedCode]`; in a trimmed
> application, register handlers with `AddHandler<THandler>()`.

### Options

`MessagingOptions` binds from the `MicroKit:Messaging` configuration section:

```json
{
  "MicroKit": {
    "Messaging": {
      "ServiceName": "shop.orders",
      "Outbox": { "BatchSize": 200, "PollingInterval": "00:00:02" },
      "Inbox": { "LeaseDuration": "00:10:00", "RetentionDays": 45 }
    }
  }
}
```

or in code, applied after the configuration:

<!-- snippet: options -->
```csharp
services.AddMicroKitMessaging(bus =>
{
    bus.Configure(options =>
    {
        options.Outbox.BatchSize = 200;
        options.Inbox.LeaseDuration = TimeSpan.FromMinutes(10);
    });
});
```

- **Precedence, lowest first:** the host's `IHostEnvironment.ApplicationName` (the service name
  only), configuration, `bus.Configure(...)`, `bus.ServiceName`. A blank value counts as absent and
  falls back to the next source. `bus.ServiceName = ""` throws; `null` leaves it unset.
- **Read once.** Configuration is read when the options are first resolved. There is no reload: a
  changed value takes effect on the next start. A container without an `IConfiguration` keeps the
  defaults.
- **Validated at start** (`ValidateOnStart`). Every value is range-checked: positive sizes and
  intervals, `PollingInterval` not above `MaxPollingInterval`, and no delay beyond what
  `Task.Delay` accepts. Each failure names its key and the accepted range, e.g.
  `MicroKit:Messaging:Outbox:BatchSize must be at least 1; it is 0.`

### What fails at host start

The composition is checked before any worker runs. Every problem is listed in one
`IntegrationEventConfigurationException`, each with its fix:

- a published or handled type without `[MessageContract]`, or with one whose arguments are invalid;
- two types claiming one wire type (`<name>.v<version>`). Two versions of one name are two contracts;
- two `AddMicroKitMessaging` calls setting different `bus.ServiceName` values;
- a publication with no `IMessageTransport` registered;
- an `IOutboxProcessorStore` or `IInboxProcessorStore` registered without the engine that drains it.

The last two are structural checks: they verify that a service is registered, not that it can be
activated. `AddMicroKitMessaging` may be called once per module of a modular monolith; the calls add
to one composition.

### With persistence — the outbox and domain events

<!-- snippet: durable-context -->
```csharp
// Your DbContext owns the outbox and inbox tables — that is what makes the write atomic.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyMessagingConfiguration();
}
```

<!-- snippet: durable-composition -->
```csharp
// Persistence: AddUnitOfWork binds IUnitOfWork / ITransactionalContext and registers the
// IDomainEventsProvider the dispatcher drains — all on the same DbContext.
services.AddMicroKitPersistence(p => p
    .AddEntityFrameworkCore()
    .AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString))
    .AddUnitOfWork<AppDbContext>());

// CQRS. TransactionBehavior (order 700) is the dispatch-and-commit owner: it dispatches domain
// events and only then commits, so outbox rows land in the same SaveChanges as the aggregate.
services.AddMicroKitMediatR(cfg => cfg
    .FromAssemblyContaining<PlaceOrderCommand>()
    .AddTransactionBehavior());

// Messaging, durable: domain events go through the outbox. No transport, and none needed.
services.AddMicroKitMessaging(bus =>
{
    bus.ServiceName = "shop.orders";
    bus.AddEfCoreOutbox<AppDbContext>();
    bus.AddMediatRDomainEvents();    // only with MicroKit.Messaging.MediatR
});
```

The durable calls live in their packages' namespaces: `MicroKit.Messaging.EntityFrameworkCore`
(`AddEfCoreOutbox`, `ApplyMessagingConfiguration`), `MicroKit.Messaging.MediatR.DependencyInjection`
(`AddMediatRDomainEvents`) and `MicroKit.MediatR.Behaviors.DependencyInjection`
(`AddTransactionBehavior`).

> **PostgreSQL and SQLite only.** `ApplyMessagingConfiguration()` declares a unique index over
> `(OriginMessageId, ContractName)` on the outbox, and that index is a model invariant rather than
> an index you may skip. **SQL Server is not supported for it**: it compares nulls as equal in a
> unique index, and every notification row carries `(NULL, NULL)` — so the second notification row
> your application ever writes is rejected. Not at DDL time, not on the first row: in production, on
> the second insert. See *State* below for the workaround you would own.

**Nothing here composes in a required order.** `UseInMemoryTransport()` — or a broker provider's
`Add{Provider}Transport()` — goes before or after `AddMediatRDomainEvents()`; either works.

A host that publishes only domain-event notifications registers no transport at all, as above. Do
**not** add one "just in case": it declares an intent to send contracts, and without a working
`IMessageTransport` behind it the outbox cannot dispatch a row of any kind — loudly, with every
batch released, nothing lost and no retry budget spent, but nothing delivered either.

> ⚠ **The inbox path needs a receiving transport.** Inbox rows are written by `IEnvelopeReceiver`,
> which a broker provider's consume loop calls — and no broker provider ships yet. A consuming host
> registers its handlers with `bus.AddHandler<THandler>()` and `AddEfCoreOutbox<TContext>()` so
> there is somewhere to write.

A host built with `Host.CreateApplicationBuilder()` or `WebApplication.CreateBuilder()` already has
logging. A bare `ServiceCollection` does not, and several types here require `ILogger<T>` — call
`AddLogging()` yourself if you compose by hand.

> **Handlers must be idempotent.** Delivery is at-least-once by design. On the bus, a message
> redelivered because one handler failed runs every handler again. On the notification path an
> outbox retry re-publishes the notification and re-runs *all* of its handlers. The same contract
> applies to `IMessageHandler<T>` on the inbox path.

---

## End to end

A complete program on the bus:

<!-- snippet: end-to-end -->
```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddMicroKitMessaging(bus =>
{
    bus.ServiceName = "shop.orders";
    bus.Publishes<OrderPlaced>();
    bus.AddHandler<OrderPlacedHandler>();
    bus.UseInMemoryTransport();
});

using var host = builder.Build();
await host.StartAsync();   // validates the options and the composition, then binds the endpoint

await using (var scope = host.Services.CreateAsyncScope())
{
    var checkout = ActivatorUtilities.CreateInstance<Checkout>(scope.ServiceProvider);
    await checkout.PlaceAsync(Guid.NewGuid());   // OrderPlacedHandler logs it, in its own scope
}

await host.StopAsync();
```

`StartAsync` validates the options and the composition, then binds the `shop.orders` endpoint to
the contracts its handlers handle. `PublishAsync` returns once the transport accepted the message;
`OrderPlacedHandler` runs in its own message scope, with the tenant, correlation and causation of
the message.

With persistence, a domain fact goes through the outbox instead:

<!-- snippet: durable-end-to-end -->
```csharp
// The fact.
public sealed record OrderPlacedEvent(Guid OrderId, Guid CustomerId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

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
        var order = Order.Place(command.CustomerId);
        order.Raise(new OrderPlacedEvent(order.Id, command.CustomerId, DateTimeOffset.UtcNow));
        await repo.AddAsync(order, ct);
        return Success(order.Id);
    }
}

// Runs after commit, from the outbox processor. Must be idempotent — it may run more than once.
public sealed class ProjectOrderHandler(IReadModel readModel)
    : INotificationHandler<OrderPlacedNotification>
{
    public Task Handle(OrderPlacedNotification n, CancellationToken cancellationToken)
        => readModel.UpsertOrderAsync(n.DomainEvent.OrderId, cancellationToken);
}
```

Sending the command writes the aggregate and one outbox row in a single transaction. `OutboxWorker`
picks the row up on its next poll and publishes it; `ProjectOrderHandler` runs then, not before.

---

## Publishing an integration event

A domain event is internal. An **integration event** is the contract you publish for other bounded
contexts, and it is a different type on purpose — an aggregate should never reference one.

<!-- snippet: publishing-contract -->
```csharp
// Business payload only. Everything about delivery — identity, tenant, correlation, timestamps —
// is assigned when the row is staged. The attribute is the wire name and is mandatory: a CLR type
// name cannot serve as one, because a consumer in another service holds a different type.
[MessageContract("shop.orders.order-placed")]   // wire type: shop.orders.order-placed.v1
public sealed record OrderPlaced(Guid OrderId, Guid CustomerId) : IIntegrationEvent;
```

Composition — the publication is declared like any other, and `AddEfCoreIntegrationEvents<TContext>()`
registers both the outbox writer and `IIntegrationEventPublisher`:

<!-- snippet: publishing-composition -->
```csharp
services.AddMicroKitMessaging(bus =>
{
    bus.ServiceName = "shop.orders";
    bus.Publishes<OrderPlaced>();
    bus.AddEfCoreOutbox<AppDbContext>();
    bus.AddEfCoreIntegrationEvents<AppDbContext>();   // the outbox writer and IIntegrationEventPublisher
    bus.UseInMemoryTransport();                       // or a broker provider's transport
});
```

### Consuming a contract you do not publish

A service that consumes a contract registers a handler of its own local type carrying the
producer's wire name — see *A service consuming another service's contract* above. There is no
separate subscription declaration: a handler is its subscription, and a consumer declares **no
source**, since it emitted nothing.

Publishing a contract also binds its name for receiving, so a modular monolith routes its own
contracts with no second declaration, and a service may handle what it publishes. What *is*
rejected, at startup, is two different types claiming one wire type.

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
Registering a transport wires the dispatcher for the first of those: `UseInMemoryTransport()` does,
and a broker provider's `Add{Provider}Transport()` is meant to, so composing a broker is one line.
No broker provider ships yet; with one, the composition would read:

```csharp
services.AddMicroKitMessaging(bus =>
{
    bus.ServiceName = "shop.orders";
    bus.AddEfCoreOutbox<AppDbContext>();
    bus.AddRabbitMqTransport();      // hypothetical: wires the transport AND the dispatcher that feeds it
});
```

**No `IMessageTransport` implementation ships in MicroKit.** A provider package supplies one, and
owes a conformance test that `SendAsync` does not return before its broker has acknowledged. The
receiving half is the other way round: `IEnvelopeReceiver` **is** implemented here, and a provider
calls it rather than writing one — see *Receiving messages* below.

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

A declared publication with no transport fails at host start, on the bus and through the outbox
alike (#187): `UseInMemoryTransport()`, or a provider's transport registration, registers the
outbox's transport dispatcher with the transport. A `Contract` row reaching a host with no
dispatcher at all — staged by an earlier deployment, say — is a missing registration: the batch is
released untouched, no retry budget is consumed, and the worker stops (see the last paragraph).

The rest of this section is about a transport that **is** registered but whose dispatcher cannot
be built — a transport whose own dependencies are missing. A `Contract` row then fails **loudly and
reversibly**: the batch is released untouched, no retry
budget is consumed, the rows stay `Pending`, and an error (event 1009) is logged on every cycle.
The worker does **not** stop — it backs off and tries again, reporting
`OutboxBatchAbortReason.DispatcherActivationFailed` — so the rows drain on the first cycle after a
build that registers a transport is deployed. A cause that clears on its own drains them with no
intervention at all.

That verdict follows the exception's type, not who throws it. It applies to any
`InvalidOperationException` except `ObjectDisposedException` thrown while the dispatcher is built,
whether it comes from Microsoft DI or from a constructor or factory in the graph.

A missing transport reaches it because Microsoft.Extensions.DependencyInjection reports a missing
dependency as `InvalidOperationException`; that is the only container this has been verified against.
Another container may report a missing dependency with a different exception type, and there a
missing transport is treated like any failed send: retried per message, and dead-lettered at
`MaxRetries`.

An exception of any other type thrown while the dispatcher is built, such as a broker client failing
to connect in its constructor with its own exception type, is treated like a failed send too: see
the table above.

A dispatcher that is **not registered at all** is different and stays fatal: the batch is released
and the worker stops, because nothing but a redeployment can fix it.

A declared publication (`bus.Publishes<T>()`) with no transport fails at host start (#187). An
application that publishes only domain-event notifications declares none and composes legitimately
without a transport; for it, whether a transport is needed depends on whether any contract row
exists, which is data rather than composition. A host that validates its container when it is built,
which the default host builders do in the `Development` environment, also fails at startup on a
transport dispatcher with no transport behind it.

### A known defect: one tenant's row can stall the whole outbox

The dispatcher is activated inside the scope of the row being dispatched, and that scope carries the
row's `TenantId`. If your composition resolves something per tenant while that graph is built — a
`DbContext` whose connection string is looked up from the tenant, a transport with per-tenant
credentials — and that lookup throws `InvalidOperationException`, activation fails for one tenant
only and is still handled as above: the whole batch is released. (Thrown as any other untyped
exception, the same failure is retried per message and dead-letters at `MaxRetries`, and no other
tenant waits.)
When the cause never clears, as with a de-provisioned tenant, the outbox does not just wait one
cycle:

- the claim orders on `CreatedAtUtc` and a released row keeps its place, so the failing row heads
  every claim and every batch aborts before anything is published;
- the outbox makes **no progress for any tenant**, notifications included;
- no retry budget moves, so the row never dead-letters, and no API steps past a `Pending` row.

The fix is owed (ADR-MSG-019). Until it ships, event 1009 (`Error`, every cycle) names the blocking
row's `MessageId`, `TenantId` and `MessageKind`, and **the way out is SQL**: dead-letter that tenant's
pending rows. That takes them out of the claim, and every other tenant drains on the next cycle.

```sql
-- PostgreSQL, default mapping: table "OutboxMessages", enums stored as strings.
UPDATE "OutboxMessages"
SET    "Status"         = 'Failed',
       "DeadLettered"   = TRUE,
       "ProcessedAtUtc" = now(),
       "ErrorMessage"   = 'Parked by an operator: tenant cannot be dispatched'
WHERE  "TenantId" = '<TenantId from event 1009>'
  AND  "Status"   = 'Pending'
  AND  NOT "DeadLettered";
```

Event 1009 renders a null `TenantId` as `(null)`, and that string matches no row: write
`WHERE "TenantId" IS NULL` instead. Only do so where other rows carry a real tenant. In a
single-tenant deployment every row's tenant is null, so the cause cannot be tenant-scoped, and the
statement would park the whole queue: fix the composition instead.

- A row a processor holds at that instant is `Processing` and is left alone. The batch releases it,
  so run the statement again if the next 1009 still names that tenant.
- Once the tenant is restored, bring its rows back one at a time with
  `IOutboxAdminStore.RequeueAsync`, which also resets their retry count.
- If the next 1009 names a row from a **different** tenant, the cause is not tenant-scoped. Fix the
  composition instead of parking rows.

### One thing worth knowing before an incident

The transport dispatcher never deserializes the payload — it travels opaque, which is what lets a
consumer holding a different CLR type read it. The consequence is that a **corrupt payload is not
detected on the way out**. It travels, and dead-letters at the consumer, in the consumer's inbox.
That is the right place for it to fail, but it means a producer-side operator can see a perfectly
healthy queue while a consumer is dead-lettering.

---

## Receiving messages

The other end of the wire. A broker provider's consume loop hands each arriving message to
`IEnvelopeReceiver`, which records one `InboxMessage` per registered consumer; `InboxProcessor`
drains them.

```csharp
// Inside a provider's consume loop.
var envelope = Decode(brokerMessage);

try
{
    var result = await receiver.ReceiveAsync(envelope, ct);

    // Only now. Everything the result counts is already committed.
    await broker.AcknowledgeAsync(brokerMessage, ct);
}
catch (InboxPayloadException)
{
    // Permanent: this process has no local type bound to that contract name, and no redelivery
    // will change that. Nothing was recorded, so the verdict has nowhere to live but the broker.
    await broker.DeadLetterAsync(brokerMessage, ct);
}
```

> **`ReceiveAsync` returning means the message is durably recorded.** Acknowledge *after* it
> completes, never alongside it. A transport that acknowledges early turns every row it has not yet
> written into a message that exists nowhere — the broker considers it delivered, this process never
> recorded it, and nothing logs the loss. It is `SendAsync`'s rule, in the opposite direction.

### The rule is about your client's configuration, not just your call order

The sample above shows an explicit acknowledgement, and moving it after the `await` is necessary but
not sufficient — because **the acknowledgement that loses the message is usually one nobody wrote**.
Stated so it cannot be satisfied vacuously: *nothing may settle the broker message except code that
runs after the returned task has completed.* A consume loop with no ack call at all can agree with
the ordering and violate the contract.

| Broker | What settles the message implicitly | What to do |
|---|---|---|
| **Kafka** | `enable.auto.commit` — **on by default**, commits offsets on a timer for messages `Consume` already returned, so it can commit while `ReceiveAsync` is in flight | `enable.auto.commit = false`, commit after the task completes |
| **RabbitMQ** | `autoAck: true` on the consume call — settles at dispatch, set once at subscribe time, far from anything that looks like an ack | subscribe with `autoAck: false`, ack after the task completes |
| **Azure Service Bus** | `ReceiveMode.ReceiveAndDelete` — settles at receive | use `PeekLock` |

`ServiceBusProcessor.AutoCompleteMessages` also defaults to true and **is safe**: it completes after
the message handler returns, so awaiting `ReceiveAsync` inside that handler satisfies the rule. The
safe default and the unsafe one both read as "auto", which is why both are named.

### The conformance test a provider owes here

`IMessageTransport` owes one too, and **it is not the same test**. That one asserts a *callee's*
return — drive `SendAsync`, observe it did not return early. This one asserts a *caller's* ordering,
so it instruments the acknowledgement rather than the seam. Neither can be enforced from this
repository: no test here can observe whether somebody else's consume loop settled before calling.

- Fail an `IEnvelopeReceiver` — a stub whose returned task does not complete until the test releases
  it — and assert that **nothing has been settled at the broker** while it is outstanding: no ack,
  no offset commit, no lock release, no delete.
- Run it against the **real client configuration the provider ships**, not a hand-rolled loop. Every
  defect in the table above lives in client settings, so a test that mocks the client away sees none
  of them.
- **A consume loop that fails it is unusable** — regardless of whether it compiles, and regardless
  of what its other tests show.

The fan-out is **contract name → local type → consumers**: the registry resolves the wire name to
*your* type, and the handler registry resolves that type to every `IMessageHandler<T>` you
registered for it. Each consumer gets its own row, keyed on `(MessageId, ConsumerType)`, and those
rows advance independently — one handler may succeed while another retries.

A redelivery is nominal, not a failure. The unique index absorbs it, `EnvelopeReceiveResult.Duplicates`
counts it, and the consumers *after* a duplicated one still get their rows.

### The transaction, and why it is the opposite of publishing

`IIntegrationEventPublisher` requires a transaction you already own and never commits.
`IEnvelopeReceiver` requires none and commits before returning. Both are right: publishing has
business writes in flight that the contract row must be atomic with, while a consume loop is the
outer boundary with nothing to join — and the broker cannot be acknowledged until the row is
already durable.

There is no atomicity *across* the fan-out, and none is wanted: a failure after the second
consumer's row means the first is recorded, the message is redelivered, the first is absorbed as a
duplicate and the rest are written.

### A contract nobody consumes

A name you understand with no `IMessageHandler<T>` registered — since #187, a contract this service
publishes and does not handle itself — writes nothing and throws nothing: a legitimate composition.
But it is also what forgetting `bus.AddHandler<THandler>()` for a type you publish produces, and the
two look identical from outside, so it is logged at `Warning` and counted as
`microkit.inbox.envelopes.unconsumed`. Alert on a sustained non-zero value.

All three inbox counters — `microkit.inbox.messages.added`,
`microkit.inbox.messages.deduplicated` and `microkit.inbox.envelopes.unconsumed` — are published
on the meter named by `MessagingDiagnostics.InboxMeterName`.
Subscribe to it or you receive nothing:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(MessagingDiagnostics.InboxMeterName));
```

---

## State

**Stable.** The outbox: atomic batch claim with an ownership token, buffered outcomes settled in one
write, full-jitter retry, dead-lettering, and the retention worker. **The inbox: the same atomic
claim, the dedup gate actually implemented, and success settled inside the handler's own
transaction.** The EF Core stores. The `MessageKind`-routed dispatch seam. The MicroKit.MediatR
glue and the domain-event → notification → outbox path.

**Not shipping yet.** No `IMessageTransport` implementation — broker providers are v2. That is the
one missing piece of the round trip: both seams exist and both are proven end to end, so a provider
supplies a `SendAsync` and drives a consume loop into `IEnvelopeReceiver`, and writes no messaging
machinery of its own.

### What the inbox guarantees

**Transactionally atomic processing for database-backed handlers.** The processed mark and any
database side effects your handler writes through the execution scope's `DbContext` commit together
or not at all — the mark is staged into your unit of work, not written after it.

It is **not** exactly-once in general. A handler that calls an external endpoint and then rolls back
calls it again on replay: database effects happen effectively once, external effects at least once.
A handler that commits no unit of work at all is **detected and reported**, not degraded silently.

`LeaseDuration` is the setting that matters: it must comfortably exceed your worst-case handler
duration. The processor logs every lease it loses at `Warning` — event 2007 when the lease was
already gone before the handler ran, 2008 when it expired while the handler was running — and a
recurring rate of either is the signal that it does not. Without them, a lease set too short is
invisible, and the system stays correct while quietly doing less work than it
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
