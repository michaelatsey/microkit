# MicroKit.Messaging

A message bus for .NET 10 — publish, consume, retry — with a transactional outbox and an idempotent
inbox as **opt-in durability**, and a broker-agnostic transport seam.

---

## Overview

The bus works with no database: a publication goes to the transport, a handler runs in a scope of
its own. A **store** makes it durable. A store is the outbox **and** the inbox of one `DbContext`:

```
command handler ──► publication ┐  enlisted, never saved by the bus
                                ├─ ONE SaveChanges commits both — or neither
aggregate changes ──────────────┘
                                        ▼ (after commit, the store's loop)
                            outbox ──► transport ──► receiving host
                                                        │
                            handler work + its inbox row ┘  ONE transaction per handler
```

- **The outbox** guarantees delivery **to the transport**, at least once, for a fact that committed —
  not delivery to a handler. A row re-sent after its lease expired is a duplicate, and the receiving
  inbox absorbs it.
- **The inbox** guards each handler with its own row, one per (message, handler): a redelivery is
  skipped, and a handler's work, its publications and its row commit together.
- **Local messages.** A domain-event notification never goes to a transport: with a store it is an
  outbox row delivered by the store's own loop, under each handler's inbox row.

**Key design points:**
- Broker-agnostic core — it defines `IMessageTransport` and ships no broker implementation
- Atomic claims (one `UPDATE … WHERE` carrying an ownership token); a lost claim writes zero rows
- Full-jitter retries (`Uniform(0, min(2^n s, cap))`), so an outage does not resynchronise the queue
- One scope per handler — a failure in one handler cannot corrupt another
- One loop per store, each with its own back-off: a store whose database is down never delays another
- Tenant-aware: `TenantId` travels on the message, never on an ambient HTTP context

---

## Packages

| Package | Description |
|---------|-------------|
| `MicroKit.Messaging.Abstractions` | Contracts, in two layers. **`MicroKit.Messaging`** — what an application names: `IIntegrationEvent`, `MessageContractAttribute`, `IMessageBus`, `IEventPublisher`, `PublishOptions`, `MessageStoreKey`, `IMessageHandler<T>`, `MessageContext`, `MessageId`, the receive and send middleware, the exceptions. **`MicroKit.Messaging.Spi`** — what an author of a store, a transport or an integration package implements: `IMessageTransport`, `MessageEnvelope`, `IEnvelopeReceiver`, `ReceiveDisposition`, the store interfaces (`IOutboxWriter`, `IInboxGuardStore`, …), `IMessageStoreSet`, `ILocalMessageSender`, `ILocalConsumerSource`, `OutboxMessage`, `InboxMessage` |
| `MicroKit.Messaging` | The bus, the in-memory transport, the receive pipeline and its inbox guard, the store engine, `AddMicroKitMessaging`, and the store SPI (`AddMessageStore`) |
| `MicroKit.Messaging.EntityFrameworkCore` | The EF Core store: `UseEntityFrameworkOutbox<TContext>()`, `ApplyMessagingConfiguration()` |
| `MicroKit.Messaging.MediatR` | Puts MicroKit.MediatR domain events on the bus as local messages: `UseMediatRDomainEvents()` |

---

## Installation

```bash
dotnet add package MicroKit.Messaging                      # the bus and the in-memory transport
dotnet add package MicroKit.Messaging.EntityFrameworkCore  # a store: the outbox and the inbox of a DbContext
dotnet add package MicroKit.Messaging.MediatR              # MicroKit.MediatR domain events on the bus
```

The bus needs nothing else: no database, no broker. Durability is a feature you add, not a
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
- **`bus.UseInMemoryTransport()`** registers the in-memory transport, its consume loop, and the
  dispatcher that sends a store's outbox rows to it.

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
- **Global.** `Outbox` and `Inbox` apply to every store; each store runs its own loops with them.
- **Validated at start** (`ValidateOnStart`). Every value is range-checked: positive sizes and
  intervals, `PollingInterval` not above `MaxPollingInterval`, and no delay beyond what
  `Task.Delay` accepts. Each failure names its key and the accepted range, e.g.
  `MicroKit:Messaging:Outbox:BatchSize must be at least 1; it is 0.`

### What fails at host start

The composition is checked before any loop runs. Every problem is listed in one
`IntegrationEventConfigurationException`, each with its fix:

- a published or handled type without `[MessageContract]`, or with one whose arguments are invalid;
- two types claiming one wire type (`<name>.v<version>`). Two versions of one name are two contracts;
- two `AddMicroKitMessaging` calls setting different `bus.ServiceName` values;
- a publication with no `IMessageTransport` registered;
- a store whose `DbContext` is not registered, or whose model lacks the messaging entities
  (`ApplyMessagingConfiguration()`), or a store missing one of its services;
- with several stores: a handler — or a local consumer — whose assembly is bound to none, and an
  assembly bound to two;
- `UseMediatRDomainEvents()` without MicroKit.MediatR, and a notification handler that cannot be a
  consumer of its own: an open generic, a factory or an instance registration, or a handler of an
  abstract notification.

`AddMicroKitMessaging` may be called once per module of a modular monolith; the calls add to one
composition.

---

## Durability — a store

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
    bus.UseEntityFrameworkOutbox<AppDbContext>();
    bus.UseMediatRDomainEvents();    // only with MicroKit.Messaging.MediatR
});
```

The durable calls live in their packages' namespaces: `MicroKit.Messaging.EntityFrameworkCore`
(`UseEntityFrameworkOutbox`, `ApplyMessagingConfiguration`), `MicroKit.Messaging.MediatR.DependencyInjection`
(`UseMediatRDomainEvents`) and `MicroKit.MediatR.Behaviors.DependencyInjection`
(`AddTransactionBehavior`). **Nothing composes in a required order**, and a host whose only messages
are domain events registers no transport at all.

**Hosts own their migrations; none ships.** The EF Core configuration is the schema's definition,
and `dotnet ef migrations add` produces it. The CHANGELOG carries each schema change a host must
migrate, with the statements a host that owns its DDL applies.

### Publishing is enlisting

With a store, `PublishAsync` adds the outbox row to the store's `DbContext` **of the current scope**
— and saves nothing. No transaction is required: the caller's `SaveChanges` commits the row with
the business data it announces, or neither; inside a delivery, the engine's save does.

- **A row still unsaved when its scope ends is lost, and reported:** event 4000 at `Error` names the
  message, its contract and its store. A row discarded with the change tracker (`DiscardChanges`,
  `ChangeTracker.Clear()`) is reported at `Debug` (4001): the caller chose to drop it. So is a row
  enlisted by a delivery's attempt that rolls back: it goes with the work it announced.
- With no store configured, `PublishAsync` sends to the transport at once (#186). With stores
  configured there is never a direct send.

### Several stores

A host may have one store per `DbContext`. A publication's store is, first match wins:

1. `PublishOptions.Store`, when set — it must name a configured store; inside a delivery, that
   delivery's store, else `IntegrationEventPublishException`;
2. inside a scope the engine opened for a delivery, that delivery's store;
3. the only store, when there is one;
4. the store the message type's assembly is bound to;
5. otherwise `IntegrationEventConfigurationException`, naming the stores and the fix.

A handler's inbox store: the only store, else the store its type's assembly is bound to — checked
at start. Binding is needed only with several stores:

```csharp
services.AddMicroKitMessaging(bus =>
{
    bus.ServiceName = "shop";
    bus.UseEntityFrameworkOutbox<OrdersDbContext>(store => store.ForAssemblyContaining<OrdersModule>());
    bus.UseEntityFrameworkOutbox<BillingDbContext>(store => store.ForAssemblyContaining<BillingModule>());
    bus.UseInMemoryTransport();
});
```

**A domain event is enlisted in the store its notification type is bound to**, so that store's
context must be saved by the same unit of work. A notification bound to `BillingDbContext`, raised
by a command whose unit of work saves `OrdersDbContext`, is never saved — and is reported (4000).

### What the inbox guarantees

With a store, each handler runs under its inbox row, keyed (`MessageId`, consumer type). The
consumer type is the handler's `Namespace.Type, AssemblyName`, with no version: it survives an
assembly-version bump, so a redelivery across a deployment still finds its row.

- a row in any state — processed, failed, retrying — skips a new delivery: a redelivery never starts
  a second attempt;
- a first delivery runs as **one operation of the store's execution strategy**, in a transaction the
  engine begins: the row, the handler's own saves, its publications and its cascade events commit
  together, whether or not the handler saved first. A commit whose outcome is unknown is verified by
  reading the row, never replayed blindly;
- a concurrent delivery that commits the row first makes this one a duplicate: rolled back, success;
- a handler that fails rolls back everything it did; its row is then written `Failed` in a
  transaction of its own, and only once that row is committed is the delivery acknowledged. From
  there the store's loop retries it. A permanent failure is dead-lettered at once, counting no
  attempt; a dependency outage of one tenant writes its row to wait out that tenant's back-off, and
  one with no tenant, or shared by every tenant, writes no row and redelivers (*One tenant down*,
  under *Sending to a broker*).

**Exactly-once covers every database effect written through the store's `DbContext`.** A replayed
attempt reruns the handler, so what it does through another connection or `DbContext`, and every
external call, is **at-least-once**: make those idempotent.

Rules for a handler with a store:

- **Do not begin your own transaction on the store's context.** The engine's transaction is open;
  `ITransactionalContext.ExecuteAsync` joins it, and its commit is the engine's.
- **`IResultUnitOfWork.TryCommitAsync` refuses inside a guarded handler** — the engine's
  transaction is open on the store's context, so it throws `InvalidOperationException`.
  `TryExecuteAsync` joins that transaction and converts nothing: a conflict propagates as an
  exception, which fails the handler and is recorded on its row. Save with `IUnitOfWork.CommitAsync`,
  or let the engine save.

`LeaseDuration` must comfortably exceed your worst-case handler duration: a retry whose claim is lost
mid-handler (event 2205) records no failure and leaves its attempt count unchanged, and a recurring
rate of it is the signal the lease is too short.

**Inbox retention is not housekeeping.** `RetentionDays` defaults to 30, deliberately unlike the
outbox's 7: the table only deduplicates messages it still holds, so the window must exceed the
maximum plausible redelivery delay of every upstream transport.

### Domain events — the local endpoint

`UseMediatRDomainEvents()` turns each domain event's notification into a **local message**: never
handed to a transport, no `[MessageContract]` needed.

- **With a store**, the notification is an outbox row routed to the local endpoint
  (`OutboxMessage.Endpoint = "local"`), committed with the aggregate. The store's loop delivers it to
  each `INotificationHandler` as a **consumer of its own**, under its own inbox row: one handler
  failing is retried alone while the others are done.
- **Without a store**, the notification is queued in memory when it is published — one entry per
  handler, each with its own retries and dead letter. It is queued **before the command commits**:
  a command that rolls back still notifies, and a message still queued is lost if the process
  stops. **Durability needs a store.**
- **Cascade events are delivered.** With a store, a domain event a handler raises — a notification
  handler's or a bus handler's — is dispatched after the handler and before the engine saves its
  work, so it commits with that work and is delivered in turn. **Without a store**, domain events
  still undispatched when a handler's scope ends are dropped and reported (2310, `Error`): there is
  no engine save after a handler to dispatch before. Durability needs a store.
- **A notification handler is activated per delivery from its implementation type**: a transient or
  scoped registration yields one instance per delivery, disposed with it, and a sibling that cannot
  be built never fails it. A singleton is rejected at start — register it transient or scoped — and
  so is a decorated registration, a factory registration: a consumer is keyed by its handler's
  type.
- A handler never runs in the scope that raised the event.

> **Handlers must be idempotent.** Delivery is at-least-once by design. Without a store, a bus
> message redelivered because one handler failed runs every handler again (#186). With a store, each
> handler is guarded by its own inbox row and only the failing one is retried — but what a handler
> does outside its store's `DbContext` is still at-least-once. A notification handler is always a
> consumer of its own: a retry re-runs only the one that failed.

A host built with `Host.CreateApplicationBuilder()` or `WebApplication.CreateBuilder()` already has
logging. A bare `ServiceCollection` does not, and several types here require `ILogger<T>` — call
`AddLogging()` yourself if you compose by hand.

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

With a store, a domain fact goes through the outbox instead:

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

Sending the command writes the aggregate and one local outbox row in a single transaction. The
store's loop picks the row up on its next pass and delivers it to `ProjectOrderHandler` under its
inbox row; the handler runs then, not before.

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

<!-- snippet: publishing-composition -->
```csharp
services.AddMicroKitMessaging(bus =>
{
    bus.ServiceName = "shop.orders";
    bus.Publishes<OrderPlaced>();
    bus.UseEntityFrameworkOutbox<AppDbContext>();   // publications are enlisted in its DbContext
    bus.UseInMemoryTransport();                     // or a broker provider's transport
});
```

The call site is any scoped service — a command handler, or a notification handler where a domain
fact becomes a published contract:

```csharp
public sealed class PublishOrderPlacedHandler(IMessageBus bus) : INotificationHandler<OrderPlacedNotification>
{
    public async Task Handle(OrderPlacedNotification n, CancellationToken ct)
        => await bus.PublishAsync(new OrderPlaced(n.DomainEvent.OrderId, n.DomainEvent.CustomerId), ct: ct);
}
```

Inside a delivery the publication is enlisted in the delivery's store and commits with the
handler's inbox row: a handler that is replayed or retried never publishes twice. Outside one, the
caller's `SaveChanges` commits it.

---

## Sending to a broker

An outbox row is dispatched by its `Endpoint`: `null` sends it to the transport, `"local"` delivers it
in process (see *Domain events*); any other value is dead-lettered. Every store comes with both
dispatchers, so a transport row is sent through whatever `IMessageTransport` the host registers —
the in-memory one (`UseInMemoryTransport()`) or a provider's. No broker provider ships yet; with
one, the composition would read:

```csharp
services.AddMicroKitMessaging(bus =>
{
    bus.ServiceName = "shop.orders";
    bus.UseEntityFrameworkOutbox<AppDbContext>();
    bus.AddRabbitMqTransport();      // hypothetical: registers an IMessageTransport
});
```

**No broker `IMessageTransport` ships in MicroKit.** A provider package supplies one, and owes a
conformance test that `SendAsync` does not return before its broker has acknowledged. The receiving
half is the other way round: `IEnvelopeReceiver` **is** implemented here, and a provider calls it —
see *Receiving messages* below.

### What `SendAsync` returning means

> **The destination has acknowledged the message.** Not that it was enqueued for background
> delivery, not that it was buffered locally.

The store's outbox loop marks the row `Published` on that return, and `Published` is terminal. A
transport that hands off asynchronously makes the mark a lie in exactly the way an outbox exists to
prevent. Writing a transport? Return after the publisher confirm (RabbitMQ), after
`SendMessagesAsync` (Azure Service Bus), after the delivery report (Kafka) — and see the conformance
obligation in `IMessageTransport`'s docs.

Failures are classified by exception type, and the type is a statement about scope:

| Thrown | Meaning | Effect |
|---|---|---|
| `OutboxTransportUnavailableException` | the broker is unreachable, so the next message will fail too | the store's transport rows deferred until the broker's back-off ends, **no retry consumed**; its local rows are still delivered |
| *anything untyped* | this one message failed while the transport is healthy | retried with jittered back-off |
| `OutboxPayloadException` | the destination rejects this content permanently | dead-lettered on first sight |

### With no transport, or one that cannot be built

A publication needs a transport registered, with or without a store: a declared publication with
none fails at host start (#187), and `PublishAsync` throws. A transport row reaching a host with no
transport — written by an earlier deployment, say — or a transport that cannot be built, its own
dependencies missing, fails **loudly and reversibly**: the transport rows are deferred untouched, no
retry is consumed, and an error (event 1009, naming the call to add) is logged each time the transport
is tried (`OutboxBatchAbortReason.DispatcherActivationFailed`) until one is deployed.

Only the transport rows wait. A down endpoint has a back-off of its own: it starts at
`Outbox.PollingInterval`, doubles on each failed attempt up to `Outbox.TransportUnavailableBackoff`,
and clears on the first row the endpoint sends. Until it ends, the endpoint's rows are left out of
the claim — neither attempted nor rewritten — and the batch that met the failure is settled back to
`Pending` with its next attempt at the end of the back-off, so the store's local rows, behind them in
the queue or not, are delivered at the normal cadence. On rows with a tenant, the endpoint backs off
once a second tenant fails too (*One tenant down*, below); until then only the first tenant waits. Event 1010 (`Warning`) says what is deferred
and until when. A broker outage works the same way, and a local consumer's dependency outage
(`OutboxTransportUnavailableException` from a local consumer) defers the local rows while the
transport rows go out. That verdict follows the exception's type: any
`InvalidOperationException` except `ObjectDisposedException` thrown while the dispatcher is built —
the type Microsoft.Extensions.DependencyInjection reports a missing dependency with. Any other type
thrown there is treated like a failed send.

### One tenant down: its messages wait, the others go out (#124)

The dispatcher is activated inside the scope of the row being dispatched, and that scope carries the
row's `TenantId`. A composition that resolves something per tenant while that graph is built — a
transport with per-tenant credentials — can fail for one tenant only, and so can a broker that
refuses one tenant (`OutboxTransportUnavailableException`). Such a verdict on a row **with a tenant**
holds that tenant, not the endpoint:

- that tenant's rows of the endpoint wait to the end of **its own back-off** — the endpoint's curve,
  from `Outbox.PollingInterval` doubling to `Outbox.TransportUnavailableBackoff` — no retry consumed,
  and the other tenants' rows go out at the normal cadence. Event 1011 (`Warning`) says, per batch and
  tenant, what waits and until when; 1009 or 1004 still say why. A success of the tenant clears it.
- **The probe.** Once a tenant is held, the next attempt from a tenant with no back-off is the probe.
  It succeeds: the cause was the first tenant's, and the endpoint stays open. It fails too: the cause
  is shared, so that tenant is held as well and the endpoint backs off for every tenant, as above
  (1010). A real outage costs at most two attempts per batch, never one per tenant.
- **Two tenants that never recover cannot hold a third.** While a tenant is in back-off, the batch
  tries the rows of tenants not known to fail first, then the others, those that failed least first;
  and once the endpoint has backed off, a held tenant failing again counts as a probe only until a
  success clears the endpoint. Claim order is not a contract: it changes while a tenant is in back-off.
- A row **with no tenant** has nothing to narrow the verdict to: its endpoint backs off as above.
- **A held tenant's rows are left out of the claim**, on that endpoint only, until its back-off ends:
  neither claimed nor rewritten meanwhile, as a down endpoint's are. When it ends, one batch of them
  is claimed: the first row is tried and, if it fails, the rest of that batch waits again — so a store
  whose rows all carry one tenant (a database per tenant) touches one batch per back-off period, not
  its whole backlog. A row with no tenant is never left out. Tenant ids are compared by the column's
  collation: under a case-insensitive one, holding `Acme` also holds `acme`. The EF store needs a
  provider that translates a collection parameter (SQL Server: compatibility level 130 or above).
- **The bound.** Each claim reads, and never writes, a held tenant's claimable rows: the filter is
  not an index range, so the cost of every claim, for every other tenant, grows with that backlog,
  not with the batch size — a slower store, not a stalled one. It is what the claim read over the same
  backlog once deferred, before tenants were left out, and adding `TenantId` to the claim indexes
  does not change it. `DeadLetterPendingAsync` (*A tenant that never recovers*, below) is the exit
  for a tenant that will not recover.

The inbox follows the same rule, on its own curve (`Inbox.PollingInterval` doubling to
`Inbox.DependencyUnavailableBackoff`), for an `InboxDependencyUnavailableException` a handler throws:

- **Retries.** A held tenant's rows wait to the end of its back-off with their attempt count
  unchanged, left out of the retry claim meanwhile, and the store's other tenants are retried. The batch is abandoned with the store-wide
  back-off, as before, only when the probe fails, for a row with no tenant, or when the store itself
  is unreachable.
- **New deliveries.** The delivery's row is written `Failed`, no attempt counted, due at the end of
  the tenant's back-off, and the delivery is acknowledged once that row has committed: the store's
  retry loop owns it, and the transport keeps delivering the other tenants. Later deliveries of the
  held tenant are written the same way without running the handler, and so is a delivery already
  running when its tenant entered back-off: it waits for the same back-off, which it neither extends
  nor turns into a shared outage. Event 2013 (`Warning`) fires when
  a tenant enters back-off and once per retry batch and tenant; each deferred delivery logs 2014
  (`Debug`), since they arrive at the broker's rate.
- **What that costs.** During a shared outage with tenants, a held tenant's deliveries move into the
  inbox table instead of waiting in the broker — CAP persists every received message the same way.
  The transport still pauses and redelivers (`PauseAndRedeliver`) when the store is unreachable, for a
  delivery with no tenant, and when the probe fails.
- A local consumer's dependency down for one tenant holds that tenant's local rows on the local
  endpoint, by the outbox's rule above; no inbox row is written for them.

**A tenant that never recovers** — a de-provisioned one — holds only itself: it is tried once per its
back-off and never dead-lettered on its own, because an automatic cap would dead-letter a long but
legitimate outage. To park it, dead-letter its waiting messages, in the outbox and in the inbox; to
bring them back once it is healthy, requeue them:

<!-- snippet: tenant-admin-exit -->
```csharp
// A tenant that will not recover: dead-letter the messages it holds, in one statement per table.
// Only the rows no loop holds are parked; run it again if the tenant's events still report rows.
public static async Task ParkTenantAsync(
    IServiceProvider services, MessageStoreKey store, string tenantId, CancellationToken ct = default)
{
    await using var scope = services.CreateAsyncScope();
    var outbox = scope.ServiceProvider.GetRequiredKeyedService<IOutboxAdminStore>(store);
    var inbox = scope.ServiceProvider.GetRequiredKeyedService<IInboxAdminStore>(store);

    await outbox.DeadLetterPendingAsync(tenantId, ParkedReason, ct);
    await inbox.DeadLetterPendingAsync(tenantId, ParkedReason, ct);
}

// The tenant is healthy again: requeue what was parked — not what dead-lettered on its own.
public static async Task RestoreTenantAsync(
    IServiceProvider services, MessageStoreKey store, string tenantId, CancellationToken ct = default)
{
    await using var scope = services.CreateAsyncScope();
    var outbox = scope.ServiceProvider.GetRequiredKeyedService<IOutboxAdminStore>(store);
    var inbox = scope.ServiceProvider.GetRequiredKeyedService<IInboxAdminStore>(store);

    foreach (var message in await outbox.GetDeadLetteredAsync(1000, tenantId, ct))
    {
        if (message.ErrorMessage == ParkedReason)
        {
            await outbox.RequeueAsync(message.Id, ct);
        }
    }

    foreach (var row in await inbox.GetDeadLetteredAsync(1000, tenantId, ct))
    {
        if (row.ErrorMessage == ParkedReason)
        {
            await inbox.RequeueAsync(new InboxMessageKey(row.MessageId, row.ConsumerType), ct);
        }
    }
}

private const string ParkedReason = "Parked by an operator: the tenant cannot be served.";
```

Both admin stores are registered under the store's key; `EntityFrameworkOutbox.KeyOf<TContext>()` is
the EF store's. A row a loop holds at that instant is left to its batch, and settled back to wait:
run `ParkTenantAsync` again if 1011 or 2013 still name the tenant.

### One thing worth knowing before an incident

The transport dispatcher never deserializes the payload — it travels opaque, which is what lets a
consumer holding a different CLR type read it. The consequence is that a **corrupt payload is not
detected on the way out**. It travels, and dead-letters at the consumer. That is the right place for
it to fail, but a producer-side operator can see a perfectly healthy queue while a consumer is
dead-lettering.

---

## Receiving messages

The other end of the wire. A broker provider's consume loop hands each arriving message to
`IEnvelopeReceiver`, which runs it through the receive pipeline — every handler of its contract, each
guarded by its inbox row when it has a store — and answers what the transport must do:

```csharp
// Inside a provider's consume loop.
var envelope = Decode(brokerMessage);
var result = await receiver.ReceiveAsync(envelope, ct);

switch (result.Disposition)
{
    case ReceiveDisposition.Acknowledge:         // every handler's outcome is durable
        await broker.AcknowledgeAsync(brokerMessage, ct);
        break;
    case ReceiveDisposition.DeadLetter:          // never deliverable by this process
        await broker.DeadLetterAsync(brokerMessage, result.Reason, ct);
        break;
    case ReceiveDisposition.Redeliver:           // not settled: the transport's retry policy
        await broker.RejectAsync(brokerMessage, requeue: true, ct);
        break;
    case ReceiveDisposition.PauseAndRedeliver:   // a dependency is down: pause, then redeliver
        await PauseAsync(ct);
        await broker.RejectAsync(brokerMessage, requeue: true, ct);
        break;
    case ReceiveDisposition.Unsettled:           // the host is stopping: settle nothing
        return;
}
```

> **`Acknowledge` means every handler's outcome is durable** — committed with its row, or its
> failure recorded on its row. Acknowledge *after* `ReceiveAsync` completes, never alongside it, and
> never on `Unsettled`: an acknowledgement that runs early turns a message this process never
> recorded into one that exists nowhere. It is `SendAsync`'s rule, in the opposite direction.

### The rule is about your client's configuration, not just your call order

**The acknowledgement that loses the message is usually one nobody wrote.** Stated so it cannot be
satisfied vacuously: *nothing may settle the broker message except code that runs after the returned
task has completed.*

| Broker | What settles the message implicitly | What to do |
|---|---|---|
| **Kafka** | `enable.auto.commit` — **on by default**, commits offsets on a timer for messages `Consume` already returned, so it can commit while `ReceiveAsync` is in flight | `enable.auto.commit = false`, commit after the task completes |
| **RabbitMQ** | `autoAck: true` on the consume call — settles at dispatch, set once at subscribe time, far from anything that looks like an ack | subscribe with `autoAck: false`, ack after the task completes |
| **Azure Service Bus** | `ReceiveMode.ReceiveAndDelete` — settles at receive | use `PeekLock` |

`ServiceBusProcessor.AutoCompleteMessages` also defaults to true and **is safe**: it completes after
the message handler returns, so awaiting `ReceiveAsync` inside that handler satisfies the rule.

### The conformance test a provider owes here

`IMessageTransport` owes one too, and **it is not the same test**. That one asserts a *callee's*
return; this one asserts a *caller's* ordering, so it instruments the acknowledgement rather than
the seam.

- Fail an `IEnvelopeReceiver` — a stub whose returned task does not complete until the test releases
  it — and assert that **nothing has been settled at the broker** while it is outstanding: no ack,
  no offset commit, no lock release, no delete.
- Run it against the **real client configuration the provider ships**, not a hand-rolled loop.
- **A consume loop that fails it is unusable**, regardless of its other tests.

The fan-out is **contract name → local type → handlers**: each handler is guarded on its own, so one
may succeed while another is retried from its row. A redelivery is nominal, not a failure: each
handler's row skips it.

### A contract nobody consumes

A name you understand with no `IMessageHandler<T>` registered — a contract this service publishes
and does not handle itself — runs nothing and is acknowledged: a legitimate composition. But it is
also what forgetting `bus.AddHandler<THandler>()` produces, so it is logged at `Warning` (3001) and
counted as `microkit.inbox.envelopes.unconsumed` (*Metrics*, below). Alert on a sustained non-zero
value.

---

## Metrics

The module publishes on two meters, named by `MessagingDiagnostics`. Subscribe to both or you receive
nothing:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddMeter(MessagingDiagnostics.InboxMeterName)
        .AddMeter(MessagingDiagnostics.OutboxMeterName));
```

An endpoint's or a tenant's outage shows as a value above zero for as long as it lasts, not as a log
level: the back-off counters count each endpoint and tenant that has not recovered (below).

**`MessagingDiagnostics.OutboxMeterName`** (`MicroKit.Messaging.Outbox`). Every instrument is tagged
`store.key` (the store's `MessageStoreKey`) and `endpoint` (`transport` or `local`), never a tenant: a
tenant's cardinality is unbounded, and events 1011 and 1009 name it.

| Instrument | Kind, unit | What it is | Alert |
|---|---|---|---|
| `microkit.outbox.messages.settled` | counter, `{message}`, tag `outcome` | The rows a batch settled: `published`, `retry`, `dead_letter`, `deferred`. Counted at least once: a partial settlement (1007) counts its whole batch, whose written rows never come back; a row whose lease was lost is counted under this processor's verdict and again under the verdict of whoever settles it, which may differ — `dead_letter` included. A settlement that wrote nothing, or threw, counts nothing: what it committed is unknown, its rows that come back are counted then, and a commit that landed without the store being able to verify it is not counted. A released row (host stop, configuration fault) is never counted | a `dead_letter` rate above 0 |
| `microkit.outbox.dispatch.delay` | histogram, `s` | From the row's staging (`CreatedAtUtc`) to its endpoint taking it, on the dispatching instance's clock, at 0 when that clock is behind. A batch whose settlement fails is re-sent and sampled again. Bucket boundaries from 0.01 s to 3600 s are advised | a high percentile above what your consumers tolerate |
| `microkit.outbox.endpoints.backoff` | up-down counter, `{endpoint}` | 1 while the endpoint has not recovered, else 0 | `microkit_outbox_endpoints_backoff > 0` for 15 min (below) |
| `microkit.outbox.tenants.backoff` | up-down counter, `{tenant}` | The tenants on the endpoint that have not recovered | `> 0` for 15 min (below) |

**`MessagingDiagnostics.InboxMeterName`** (`MicroKit.Messaging.Inbox`). No tenant tag either: event 2013
names the tenant.

| Instrument | Kind, unit | What it is | Alert |
|---|---|---|---|
| `microkit.inbox.messages.added` | counter, `{message}`, tag `consumer.type` | A new delivery whose handler wrote its inbox row, `Processed` or `Failed` | — |
| `microkit.inbox.messages.deduplicated` | counter, `{message}`, tag `consumer.type` | A redelivery the inbox absorbed. Normal: watch the rate, not the value | a sustained climb in the rate |
| `microkit.inbox.envelopes.unconsumed` | counter, `{message}`, tag `contract.name` | An envelope no handler consumes (above) | a sustained non-zero value |
| `microkit.inbox.tenants.backoff` | up-down counter, `{tenant}`, tag `store.key` | The tenants on the store's inbox that have not recovered | `> 0` for 15 min (below) |

The back-off counters are observable up-down counters: their values add up across stores and endpoints,
and Prometheus shows them as gauges. They read the engine's memory, never the database. Each counts an
endpoint or a tenant 1 from a down verdict until a success, or until a ceiling plus `MaxPollingInterval`
after its back-off ended with no new attempt — for the outbox, `TransportUnavailableBackoff` +
`OutboxProcessorOptions.MaxPollingInterval`; for the inbox, `DependencyUnavailableBackoff` +
`InboxProcessorOptions.MaxPollingInterval` (6 minutes with the defaults). A back-off that has ended is
half-open, not recovered: the next attempt comes within `MaxPollingInterval` plus a batch's own time, so
an endpoint or a tenant still failing is blocked again before that bound, and the counter never drops to
0 during an outage; one with nothing left to send drops out after it. The inbox has one exception: a
transport delivery that leaves its tenant in back-off with no row — the tenant fails the probe, its
deferred row cannot be written, the store is unreachable while writing it, or the host stops while
writing it — is next attempted by the transport's redelivery, not the retry loop, and a provider pausing
longer than the bound reads 0 before it. A tenant also drops out when its entry is pruned, a ceiling
after its back-off ended, by the next batch of its store: with `MaxPollingInterval` above the ceiling, a
tenant still failing can read 0 for the batch that prunes it and blocks it again. Each process reports
its own view, and one that recorded the last failure, at the ceiling, and has nothing left to send —
another instance drained the rows — holds 1 for up to two ceilings plus `MaxPollingInterval` after that
failure (11 minutes with the defaults). Alert on the raw value held longer than that:
`microkit_outbox_endpoints_backoff > 0` for 15 minutes with the defaults. An outbox store is reported
from its first cycle. An inbox store is reported once its back-off state is created, by the first
`InboxTenantBackoff.For` call for it, which a local delivery never makes: its retry loop's first cycle;
a dependency outage, or the store unreachable, met by one of its transport deliveries; or, while some
store's inbox is not quiet — a tenant in back-off, a probe pending or a suspicion standing — one of its
transport deliveries with a tenant that passed the dedup read, or a transport success. So it can read 0
with no outage of its own. Every known store reports, zeros included, so an alert resolves; a store none
of these reaches reports nothing (event 1400 covers a processor that never activates). A configuration
fault needs no metric: it is logged at `Critical` on every cycle (1006, 1402).

An outage with no endpoint or tenant to hold in back-off shows on no counter: a store's database
unreachable (the loop backs off, 1401), and an inbox dependency down for a message with no tenant (a
retry batch is abandoned, 2004; a new delivery pauses its transport). Those are reported by their events
and the loops' delays.

The instruments do not follow the OpenTelemetry messaging semantic conventions, which have
Development status.

---

## State

**Stable.** The bus and its in-memory transport. The outbox: atomic batch claim with an ownership
token, buffered outcomes settled in one write, full-jitter retry, dead-lettering, retention. The
inbox guard on one receive pipeline for transport deliveries, local deliveries and retries. The EF
Core store, several stores per host, one loop per store. The local endpoint and the MicroKit.MediatR
bridge.

**Not shipping yet.** No broker `IMessageTransport`: no broker provider exists yet. Both seams exist and are proven end to end, so a provider supplies a `SendAsync` and
drives a consume loop into `IEnvelopeReceiver`, and writes no messaging machinery of its own.

**Known defects.**
- The store set is read once, at start: a store per tenant database is #191.

**Tested providers.** PostgreSQL (through Testcontainers) and SQLite. The outbox schema no longer
carries the replay index that excluded SQL Server, but SQL Server is not tested.

All packages are `1.0.0-preview.*`. Contracts may still change between previews.
