# microkit-messaging-architecture

Load before a change to the module's layering, its packages, the processing decomposition, the
execution scope, or a decision. The engine's rules and contracts are in
`microkit-messaging-outbox-inbox.md`; package edges in `microkit-messaging-dependencies.md`; names in
`microkit-messaging-naming.md`. The decisions since ADR-MSG-010 are in
`.claude-context/context/microkit-messaging-architectural-decisions.md` — **ADR-MSG-020 is the
current model** (#188). The five decisions that have no entry there are stated at the end of this
file.

---

## Packages

| Package | Holds | Public surface |
|---|---|---|
| `MicroKit.Messaging.Abstractions` | Contracts only, in **two namespaces, one package**. `MicroKit.Messaging`: what an application names (`IIntegrationEvent`, `MessageContractAttribute`, `IMessageBus`, `IEventPublisher`, `PublishOptions`, `MessageStoreKey`, `IMessageHandler<T>`, `MessageContext`, the send and receive middlewares, `IMessageScopeInitializer`, `MessageId`, the application exceptions). `MicroKit.Messaging.Spi`: what a store, transport or integration package implements (`IMessageTransport`, `MessageEnvelope`, `IEnvelopeReceiver`, `ReceiveDisposition`, the store interfaces, `IMessageStoreSet`, `ILocalMessageSender`, `ILocalConsumerSource`, the rows, claims and outcomes) | all of it |
| `MicroKit.Messaging` (Core) | The bus, the receive pipeline and its inbox guard, the in-memory transport, the store engine, the local endpoint, composition and its start validation, the store SPI's registration (`AddMessageStore`, `MessageStoreBuilder`, `MessagingIntegrationExtensions`) | composition only: `MessagingBuilder`, `ServiceCollectionExtensions`, `MessagingDiagnostics`, `InMemoryBroker`, the options, the SPI registration types. Everything else is `internal` |
| `MicroKit.Messaging.EntityFrameworkCore` | The EF Core store: `UseEntityFrameworkOutbox<TContext>()`, `EntityFrameworkOutbox.KeyOf<TContext>()`, `ApplyMessagingConfiguration()`, the two entity configurations. `EfOutboxStore`, `EfInboxStore` are internal | composition and model |
| `MicroKit.Messaging.MediatR` | `UseMediatRDomainEvents()`: MicroKit.MediatR domain events on the bus as local messages | one extension method |

The surface is pinned name by name by `PublicSurfaceTests`; widening it is a deliberate edit there.
No broker transport ships: a provider package supplies an `IMessageTransport` and drives a consume
loop into `IEnvelopeReceiver`. Core ships exactly one `IMessageTransport`, the internal in-memory one
(`OnlyCoreShipsAnIMessageTransport_AndItIsTheInternalInMemoryOne`).

---

## Decomposition

```
AddMicroKitMessaging(bus => …)          one MessagingComposition, shared by every call
  └ MessagingCompositionValidator       hosted service registered FIRST: every composition fault
                                        is listed, with its fix, before any loop or endpoint starts

Send side
  IMessageBus.PublishAsync ── MessageStoreResolver ─┬─ no store: IMessageTransport.SendAsync
                                                    └─ store: IOutboxWriter (keyed) — enlisted, never saved
  ILocalMessageSender.SendAsync ────────────────────┬─ no store: LocalQueue → LocalQueueWorker
                                                    └─ store: outbox row, Endpoint = "local"

Store engine (one hosted service, registered with the first store)
  MessageStoreEngine : BackgroundService              when — reads IMessageStoreSet once
    per partitioned store: StoreInstanceReconciler     instances from its source, reconciled on change;
                                                       the four loops below per instance, every scope
                                                       prepared by its instance initializer, their
                                                       batches through one FifoGate (#191)
    per single store (where):
      OutboxLoop ─► IOutboxProcessor (keyed by store) ─► IOutboxDispatcher (keyed by row endpoint)
                                                           ├─ TransportOutboxDispatcher → IMessageTransport
                                                           └─ LocalOutboxDispatcher ──┐
      InboxLoop  ─► IInboxProcessor  (keyed by store) ────────────────────────────────┤
      OutboxRetentionLoop, InboxRetentionLoop                                         │
                                                                                      ▼
Receive side                                                          MessageReceivePipeline
  IEnvelopeReceiver (EnvelopeReceiver) ──────────────────────────────► DeliverAsync
  InMemoryTransportWorker ───────────────────────────────────────────► DeliverAsync
  LocalOutboxDispatcher, LocalQueueWorker ───────────────────────────► DeliverLocalAsync
  InboxProcessor ────────────────────────────────────────────────────► RetryAsync
                                     per handler: own scope → initializers → middlewares → handler,
                                     guarded by IInboxGuardStore (keyed) when the handler has a store
```

| Role | Type | Rule |
|---|---|---|
| **When** — lifecycle, cadence, cancellation | `MessageStoreEngine` and its four loops per store | Only `IServiceScopeFactory` (and singletons) in the constructor. Each loop owns its back-off and creates a scope per cycle, so a store whose database is down backs off alone. |
| **Where** — which stores | `IMessageStoreSet`; for a partitioned store, its `IMessageStoreInstanceSource` | The configured stores, static: Core names the static set only through the interface (`Core_DependsOnTheStaticStoreSet_OnlyThroughIMessageStoreSet`). A partitioned store's instances — its databases — come from its source and are reconciled at runtime by `StoreInstanceReconciler`; they never appear in `IMessageStoreSet`, whose readers keep its meaning (ADR-MSG-021). |
| **What** — one batch | `OutboxProcessor`, `InboxProcessor` (internal, keyed by store, built over the store's keyed services) | Topology-agnostic. The batch store comes from the loop's scope; every row runs in a scope of its own. |
| **Deliver** | `IOutboxDispatcher` (internal; transport and local), `MessageReceivePipeline` | Dispatch routes on `OutboxMessage.Endpoint`, never on a payload's CLR type. Every handler — transport, local, retry — goes through the one pipeline. |
| **Guard** | `IInboxGuardStore` | Resolved from each attempt's own scope, so it shares the handler's `DbContext`. |

Hosted services, all registered on demand: the validator (`AddMicroKitMessaging`), the engine
(first store), `InMemoryTransportWorker` (`UseInMemoryTransport()`), `LocalQueueWorker` (first
`AddLocalConsumerSource`). A host with none of the last three runs only the validator.

### Invariants of the decomposition

- **One scope per handler and per outbox row**, never shared across a batch: a `DbContext` fault on
  one cannot corrupt the next, and the tenant is fresh every time.
- **The store instance and key come from the scope.** Processors and the guard are resolved keyed by
  `MessageStoreKey` from a scope; nothing captures a store instance or a `DbContext` type at
  registration.
- **Nothing writes inbox rows from the dispatch path.** Rows are written by the pipeline's guard, on
  the receiving side (`NoOutboxDispatcherWritesInboxRows`).
- **Ambient context comes from the scope, never from an `AsyncLocal`.** The bus is scoped and reads
  the scope's `IExecutionContext`.
- **Activation is not registration.** A missing registration fails at start; a registered service
  whose construction throws is reported as an activation failure (1400, or 1009 for the outbox
  dispatcher) with back-off.

---

## The execution scope — ADR-EXEC-001 inversion

Messaging needs each message to run in a contextualised scope (tenant, correlation, causation)
without knowing what a tenant is. The contract lives in `MicroKit.Execution.Abstractions` (Level 0,
`Microsoft.Extensions.DependencyInjection.Abstractions` only):

- `IExecutionScopeFactory.CreateScopeAsync(IExecutionContext context, CancellationToken ct = default)`
  → `ValueTask<IExecutionScope>`; asynchronous because a tenant-aware factory may do I/O.
- `IExecutionScope : IAsyncDisposable` — wraps the service scope.
- `IExecutionContext` — `TenantId` (`string?`), `CorrelationId`, `CausationId`, a property bag. No
  business notion of tenant.

Core consumes the factory everywhere a message scope is opened (`OutboxProcessor`,
`MessageReceivePipeline`). Its default is `PassThroughExecutionScopeFactory` (internal, registered
with `AddSingleton`, so a host registration added after it wins). **Messaging does not reference
Tenancy**: a tenant-aware factory is implemented elsewhere and composed by the host; none ships in
MicroKit today.

How the context reaches a service:

- `IExecutionContext` is scoped and resolves **through** `ExecutionContextHolder`. The factory (and
  the pipeline, for a substituted factory) writes the holder before anything is resolved, so a
  service taking `IExecutionContext` by **constructor** sees the message's context. Registering the
  context directly would leave constructor-injected consumers with a fresh correlation and a null
  tenant.
- Outside the pipeline, the holder's default is one correlation per DI scope, tenant and causation
  null. A host replacing `IExecutionContext` with its own scoped registration bypasses the holder
  and owns hydration.
- The pass-through scope's provider forwards keyed resolution: the stores are resolved by key from
  message scopes.
- `DeliveryStoreHolder` (the delivery's store) is written by the engine on the scope it received,
  **never** by the factory, because a host may replace the factory.
- **`IMessageStoreInstanceInitializer`** (#191) prepares every scope opened for an instance of a
  partitioned store — the loops', each row's dispatch, each delivery's and attempt's — right after its
  store and instance are written on `DeliveryStoreHolder`, and before the `IMessageScopeInitializer`s.
  Loop scopes run it alone.
- **`IMessageScopeInitializer`** is the per-message hook for anything that configures the scope —
  a tenant's connection string, say. Initializers run after the context is written and **before an
  attempt's transaction begins**, because the store's `DbContext` is built from that scope.

---

## The MediatR carve-out — ADR-MSG-009

`MicroKit.Messaging.MediatR` is the **only** Messaging package that references MediatR (transitively,
through `MicroKit.MediatR`; there is no direct `MediatR` or `MediatR.Contracts` reference).
Abstractions, Core and EntityFrameworkCore stay MediatR-free, enforced by
`Abstractions_HasNoMediatRDependency`, `Abstractions_HasNoMediatRContractsDependency`,
`Core_HasNoMediatRDependency`, `Core_HasNoMediatRContractsDependency`,
`AllAssemblies_HaveNoMediatRContractsDependency` (the glue excluded by design) and
`EfCore_HasNoMediatRDependency`. Messaging transports `IIntegrationEvent`, never `INotification`.

The glue is built on the **public SPI only** — Core grants it no `InternalsVisibleTo`, and it does
not depend on `IOutboxDispatcher` (asserted by `NoOutboxDispatcherWritesInboxRows`).
`UseMediatRDomainEvents()` is idempotent, order-independent with `AddMicroKitMediatR()`, and makes
three registrations:

1. `DomainEventNotificationSink`, an `IDomainEventsSink` (`TryAddEnumerable`): each drained domain
   event's notification is sent through `ILocalMessageSender`, identified by the event's `EventId`
   and dated by its `OccurredAt`.
2. `NotificationHandlerSource`, an `ILocalConsumerSource`: each closed `INotificationHandler<T>` of a
   domain-event notification is a local consumer **of its own**, named by its implementation type's
   version-free key (`MessageConsumerKey`), activated per delivery. An open generic, a handler of an
   abstract or interface notification, a factory or instance registration and a singleton are start
   failures, never skips.
3. `DomainEventsDispatchMiddleware`, a receive middleware: after every handler — bus or notification —
   on a host with a store, it calls `IDomainEventsDispatcher.DispatchEventsAsync`, so cascade events
   commit with the handler's work and are delivered. With no store there is no engine save after a
   handler: it dispatches nothing and reports the events left pending (2310, `Error`). The store set
   decides per delivery — registration cannot, `UseMediatRDomainEvents()` being order-independent
   with the store calls.

A start check also requires MicroKit.MediatR's `IDomainEventsDispatcher` and
`IDomainEventNotificationFactory`.

---

## Decisions held in this file

These decisions are cited by code and other rules but have no entry in the ADR file. Each states
the decision as it holds today.

### ADR-EXEC-001 — `MicroKit.Execution.Abstractions` and the scope inversion

A targeted Level 0 package for `IExecutionScopeFactory` / `IExecutionScope` / `IExecutionContext`,
not a `MicroKit.Abstractions` god-package. Messaging consumes the factory, a tenancy module
implements a tenant-aware one, the host composes: no `Messaging ↔ Tenancy` edge, and Core stays
free of Tenancy, Auth and MediatR. `IExecutionContext` stays generic. The pass-through default
lives in Core until a second, non-Messaging consumer justifies its own package. Deferred:
the tenant-aware implementation, W3C `traceparent` hydration, shard and region contexts.

### ADR-MSG-002 — processing decomposition

When / where / what are separate roles (see *Decomposition*). The processor is topology-agnostic
and shared by every store. Within a store the claim is **cross-tenant**: the batch store is resolved
from the loop's scope, never from a per-message scope, `TenantId` travels on the row, and each row's
scope is contextualised from it. The roles' types are internal; a database per tenant is a
partitioned store (#191, ADR-MSG-021): its instances come from its source, and each gets these roles'
loops; `IMessageStoreSet` keeps listing the configured stores.

### ADR-MSG-003 — inbox delivery guarantee

Delivery is at-least-once and **handlers must be idempotent** — a documented contract of the
library. With a store, ADR-MSG-020 §5 narrows what that costs: the handler's database effects
through the store's `DbContext`, its publications and its inbox row commit in one transaction the
engine begins on that context, so those effects are exactly-once. Everything else a handler does is
still at-least-once. Inbox and outbox share one retry shape: `MaxRetries`, full-jitter back-off,
dead-lettering — on first sight for a permanent failure.

### ADR-MSG-008 — `OutboxMessageFactory`

- In Core (it needs `IMessageSerializer`; the EF package has none), `internal sealed`, a singleton.
  No mapper interface until a second implementation exists.
- `CreateContract(payload, messageId, contractName, source, occurredOnUtc, headers)` for a
  publication; `CreateLocal(payload, messageId, occurredOnUtc, source, IExecutionContext)` for a
  local message. The execution context is a **method parameter**, never constructor-injected: a
  scoped dependency in a singleton is captured once and never refreshed.
- `EventType` is the payload's runtime assembly-qualified type, never `typeof(T)`.
- `MessageId` is intrinsic: the bus mints a publication's, the sender supplies a local message's
  (a domain event's `EventId`). It is the inbox key and is never regenerated downstream.
- Metadata is stamped **once, at staging**: `TenantId` passed through as is — null is valid, and
  enforcing a tenant is the host's concern — correlation substituted when absent or not a GUID,
  causation null when absent or not a GUID. From then on the columns are the source of truth; the
  transport dispatcher builds the envelope from them and never reads the payload.

### ADR-MSG-009 — the MediatR carve-out and notification idempotency

`MicroKit.Messaging.MediatR` is the single Messaging package allowed to reference MediatR (see
above). A notification handler reached through it is a consumer of its own: with a store it is
guarded by its own inbox row, so a retry reruns only the handler that failed and its effects through
the store's `DbContext` are exactly-once; without a store it has its own in-memory entry. Effects
outside the store's context remain at-least-once, so **notification handlers must be idempotent**,
as inbox handlers are.
