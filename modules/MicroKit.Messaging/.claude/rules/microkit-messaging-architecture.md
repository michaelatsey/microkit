# MicroKit Architectural Decisions - Execution Scope & Messaging Topology

> To merge into `.claude-context/context/microkit-architectural-decisions.md`.
> Reconcile ADR numbering against the existing file before committing.

---

## ADR-EXEC-001 - MicroKit.Execution.Abstractions (Level 0) + dependency inversion

### Status
Accepted.

### Context
Messaging background processors (Outbox/Inbox) must run each message inside a contextualized
execution scope (tenant, correlation; later shard/region). Hydrating a tenant context inside
Messaging would create `Messaging -> Multitenancy`, and transitively `Messaging -> Auth` - an
unacceptable coupling. Contextualized scope creation is neither an Outbox concept nor a
Multitenancy concept: it is a cross-cutting primitive.

ADR-008 previously declined a god-package `MicroKit.Abstractions` because no proven cross-cutting
contract with a real second consumer existed. That trigger is now met: a transverse contract
(`IExecutionScopeFactory`) consumed by Messaging and implemented by Multitenancy.

### Decision
1. Create a targeted Level 0 package `MicroKit.Execution.Abstractions`. Explicitly reject a
   god-package `MicroKit.Abstractions` (package-level ISP violation, opaque graph, version
   coupling, magnet effect).
2. Dependency inversion: Messaging consumes `IExecutionScopeFactory`; Multitenancy implements the
   tenant-aware variant; the host app composes. No direct `Messaging <-> Multitenancy` link.
3. `IExecutionContext` stays generic (`TenantId` as `string?`, `CorrelationId`, `CausationId`,
   property-bag). No business notion of tenant. The Multitenancy implementation translates
   `IExecutionContext.TenantId` into its `ITenantContext`.
4. Place the contract correctly before first release. `MicroKit.Messaging.Abstractions` is merged
   to dev but not yet released; placing `IExecutionScopeFactory` there and relocating it to Level 0
   in Phase 3 would be a breaking change on a released package. Level 0 is the correct long-term
   home (`Multitenancy -> Execution.Abstractions` is clean; `Multitenancy -> Messaging.Abstractions`
   would be inverted).

### v1 scope
- `IExecutionScopeFactory`
  - Proposed signature (to finalize with api-reviewer):
    `ValueTask<IExecutionScope> CreateScopeAsync(IExecutionContext context, CancellationToken ct = default)`
    - async because future hydration may perform I/O (e.g. per-tenant connection resolution).
  - `IExecutionScope : IAsyncDisposable` - wraps the `IAsyncServiceScope` + ambient context,
    disposes both.
- `IExecutionContext` - generic bag: `TenantId (string?)`, `CorrelationId`, `CausationId`,
  property-bag extensibility.
- Default pass-through implementation: trivial, wraps `IServiceScopeFactory`, zero hydration. Lives
  in Messaging Core in v1 (sole consumer). Extract to a `MicroKit.Execution` (Core) package only if
  a second non-Messaging consumer appears.
- Package dependency: `Microsoft.Extensions.DependencyInjection.Abstractions` only.

### Deferred
- Tenant-aware `IExecutionScopeFactory` implementation -> `MicroKit.Multitenancy(.EFCore)`.
- `CorrelationContext` / W3C `traceparent` hydration -> Observability integration.
- `Shard` / `Region` / `Environment` contexts.

### Consequences
- Messaging Core stays MediatR-free, Multitenancy-free, Auth-free.
- Graph: `Messaging.Core -> Execution.Abstractions`; `Multitenancy -> Execution.Abstractions`.
  No cycle.

---

## ADR-MSG-002 - Outbox/Inbox processing decomposition (Worker / Coordinator / Processor)

### Status
Accepted.

### Context
MicroKit must eventually support Single-Tenant, Shared-DB Multi-Tenant, DB-Per-Tenant, later
Sharding / Multi-Region. Folding all of this into Messaging Core would couple Core to Multitenancy
and Persistence (per-tenant DbContext) and drown the package UX. The near-term consumer (SaaS BTP
on Supabase) is Shared-DB.

A collapsed `OutboxProcessor : BackgroundService` single-topology design was considered and
rejected: it cannot host a second topology additively without either breaking the public surface
or reimplementing the loop.

### Decision
Decompose into three roles, Shared-DB by default in Core, tenant-aware topologies in a deferred
integration package.

```
OutboxWorker : BackgroundService     (internal sealed) - when: loop + cadence + IServiceScopeFactory
  -> IOutboxCoordinator              (public)          - where: topology / which DBs
        SharedDbOutboxCoordinator    (Core, default, internal sealed)
        PerTenantOutboxCoordinator   (MicroKit.Messaging.Multitenancy, deferred)
     -> IOutboxProcessor             (public)          - what: process one batch in current scope
          OutboxProcessor            (Core, internal sealed)
            -> IOutboxProcessorStore       (GetPending / lease / mark)
            -> IExecutionScopeFactory      (scope per message)
            -> IOutboxDispatcher           (runtime-type deserialize + publish)
```

Inbox is symmetric:
`InboxWorker : BackgroundService -> IInboxCoordinator -> IInboxProcessor -> IInboxStore / IExecutionScopeFactory / resolved handler`.

Role definitions:
- Worker (when): `BackgroundService`. Lifecycle, cadence (poll/wake), cancellation.
  Topology-agnostic. Holds `IServiceScopeFactory` (the only allowed place per the BackgroundService
  rule). `internal sealed`.
- Coordinator (where): topology strategy. `RunBatchAsync(ct)`. SharedDb = one scope, cross-tenant
  reservation. PerTenant (deferred) = loop `ITenantSource`, one scope per tenant. Public interface;
  concrete impls `internal sealed`.
- Processor (what): topology-agnostic batch engine. drain -> lease -> per-message scope
  (`IExecutionScopeFactory`) -> dispatch/handle -> mark / retry back-off / dead-letter. Public
  interface, single Core impl, injected by all coordinators (so the per-tenant package reuses the
  engine, never reimplements it). `internal sealed` impl.

### Public surface (v1)
- Public: `IOutboxCoordinator`, `IOutboxProcessor`, `IOutboxDispatcher` (+ inbox symmetry) + DI
  registration extensions.
- Internal sealed: `OutboxWorker`, `SharedDbOutboxCoordinator`, `OutboxProcessor`,
  `InProcessIntegrationDispatcher` (+ inbox symmetry).
- Rationale: the deferred PerTenant coordinator (separate assembly) must compose the public
  `IOutboxProcessor` / `IInboxProcessor`, never duplicate the engine.

### v1 scope (Messaging Core, Shared-DB)
- `OutboxWorker : BackgroundService`: poll/wake loop, cadence, cancellation. Delegates to
  `IOutboxCoordinator`.
- `SharedDbOutboxCoordinator`: single scope, cross-tenant reservation; calls `IOutboxProcessor`.
- `OutboxProcessor`: drain `IOutboxProcessorStore` -> lease -> `IExecutionScopeFactory.CreateScopeAsync`
  (per message in v1) -> `IOutboxDispatcher.DispatchAsync` -> `MarkPublished` / retry back-off
  (`2^RetryCount` s, cap 3600) / `DeadLetter`. Payload-agnostic.
- `IOutboxDispatcher` (Abstractions) + `InProcessIntegrationDispatcher` (Core): deserialize by
  `EventType` (runtime type, `evt.GetType()`, never `typeof(T)`) -> `IMessagePublisher.PublishAsync`.
- Shared-DB model: `GetPendingAsync(batchSize, ct)` with no `tenantId` (cross-tenant reservation);
  `TenantId` travels on the row; the scope contextualizes via `IExecutionContext.TenantId`.
- Transactional outbox atomicity: `IOutboxWriter.AddAsync` tracks the `OutboxMessage` in the
  consumer's DbContext; the row commits in the same `SaveChanges`/transaction as business state
  (driven by the MediatR glue `TransactionBehavior`). `MicroKit.Messaging.EntityFrameworkCore` ships
  the entity configuration applied to the consumer's DbContext. `OutboxMessage`/`InboxMessage` carry
  NO tenant global query filter (infrastructure tables, read cross-tenant by the processor).
- Lease/locking: optimistic lease via `ExecuteUpdateAsync`, internal to the store. No orthogonal
  `IOutboxLockingStrategy` seam in v1 (the lock mechanism is coupled to the reservation control-flow;
  an "orthogonal" seam would leak). Portable across PostgreSQL and SqlServer.
- Ingestion (`InProcessMessagePublisher`): writes one `InboxMessage` per subscribed `ConsumerType`;
  dedup absorbed in `EfInboxStore.AddAsync` (unique constraint + `DbUpdateException` as authoritative
  guard); never calls a handler directly.
- `InboxProcessor` pure drain: GetPending -> lease -> deserialize -> resolve handler by
  `ConsumerType` -> `HandleAsync` -> MarkProcessed/MarkFailed. No `ExistsAsync`/`AddAsync` inside the
  loop.
- Core stays MediatR-free, EFCore-free, Multitenancy-free, Auth-free (enforced by ArchitectureTests).

### Deferred (additive, outside Core)
- `ITenantSource` (-> `MicroKit.Multitenancy.Abstractions`): tenant discovery; consumed only by the
  per-tenant coordinator. Exact contract (pagination, has-pending filter, connection info) NOT
  specified now - reserved for the per-tenant work.
- `PerTenantOutboxCoordinator` / `PerTenantInboxCoordinator` + per-tenant DbContext/connection
  resolution -> `MicroKit.Messaging.Multitenancy` (integration: depends on Messaging + Multitenancy
  + Persistence). Reuses the public `IOutboxProcessor` / `IInboxProcessor` from Core.
- Tenant-aware `IExecutionScopeFactory` impl (per-tenant connection) -> Multitenancy.
- `IOutboxLockingStrategy` extraction - only when a second mechanism (e.g.
  `SELECT ... FOR UPDATE SKIP LOCKED`) is actually needed.
- Per-tenant scope grouping (`GroupBy(TenantId)`) - optimization once the hydrating impl exists.
- Optimizations: signal-driven wake (`Channel` + PostgreSQL `LISTEN/NOTIFY`) vs polling; W3C
  `traceparent`; source-generated JSON.

### Consequences
- Adding a topology later = additive integration package: zero breaking change on the already-shipped
  public surface AND zero engine reimplementation (the per-tenant coordinator composes the public
  `IOutboxProcessor`).
- Non-negotiable v1 discipline: concrete impls `internal sealed`, public surface minimal (the three
  seam interfaces + DI extensions).

---

## ADR-MSG-003 - Inbox delivery guarantee

### Status
Accepted.

### Context
`HandleAsync` and `MarkProcessed` are not atomic -> a crash between them + lease expiry -> handler
re-execution. Ingestion-time dedup prevents duplicate rows, not re-execution.

### Decision
Option B - at-least-once with documented idempotent handlers.
- Handlers MUST be idempotent; this is a documented contract of the library, not an implementation
  guarantee.
- Rejected Option A (exactly-once via `HandleAsync` + `MarkProcessed` in one transaction): it would
  couple handler effects to the library's DbContext/transaction, breaking Core's
  persistence-agnostic design.

### Inbox failure symmetry
Symmetric with Outbox: `MaxRetries` + back-off + dead-letter on the Inbox side (same back-off policy
as Outbox: `2^RetryCount` s, cap 3600). `MarkFailed` is the per-attempt transition; dead-letter is
terminal after `MaxRetries`.

### Consequences
- Handler idempotency is part of the public contract and must be documented in the Messaging README
  and XML docs.
- Inbox and Outbox share the retry/back-off/dead-letter shape, simplifying the engine and the
  operator mental model.

---

## Packages - net view

| Package | Status | Role |
|---------|--------|------|
| `MicroKit.Execution.Abstractions` | new (v1) | `IExecutionScopeFactory`, `IExecutionContext` (Level 0, dep DI.Abstractions only) |
| `MicroKit.Messaging.Abstractions` | patch | add `IOutboxDispatcher`, `IOutboxCoordinator`/`IInboxCoordinator`, `IOutboxProcessor`/`IInboxProcessor`; remove `tenantId` from `GetPendingAsync` |
| `MicroKit.Messaging` (Core) | in progress | Worker + SharedDb coordinator + Processor engine + in-process dispatch + pass-through scope factory |
| `MicroKit.Messaging.EntityFrameworkCore` | planned | EF stores (Shared-DB reservation, optimistic lease, inbox dedup); outbox/inbox entity configuration without tenant filter |
| `MicroKit.Messaging.MediatR` (glue) | planned | `DomainEventsDispatcher`, `MediatorOutboxDispatcher`, `TransactionBehavior`; sources current `TenantId`, passes it explicitly to `IOutboxWriter.AddAsync` |
| `MicroKit.Multitenancy(.Abstractions/.EFCore)` | deferred | `ITenantSource`, tenant-aware `IExecutionScopeFactory` impl |
| `MicroKit.Messaging.Multitenancy` | deferred | PerTenant coordinators (integration, reuses Core `IOutboxProcessor`/`IInboxProcessor`) |

---

## ADR-MSG-008 — OutboxMessageFactory construction: metadata origin, TenantId nullability, stateless design, placement

### Status
Accepted.

### Context
OutboxMessage must be constructed from three sources: the event payload (intrinsic), the ambient
execution context (TenantId, CorrelationId, CausationId), and the serializer. The placement
question (Core vs EFCore vs caller), whether TenantId is mandatory, and whether an abstraction
(IOutboxMessageMapper) is warranted were all contested.

### Decision

1. **Placement: MicroKit.Messaging (Core), not MicroKit.Messaging.EntityFrameworkCore.**
   EFCore has no IMessageSerializer dependency. The factory belongs in Core where both
   IMessageSerializer and IExecutionContext are already first-class dependencies.

2. **No IOutboxMessageMapper interface until a second implementation exists.**
   YAGNI. A direct `OutboxMessageFactory` sealed class is sufficient for v1. An interface is
   additive later — it is not a breaking change to introduce one.

3. **Signature:**
   `OutboxMessage Create(object payload, Guid messageId, DateTimeOffset occurredOnUtc, IExecutionContext context)`
   - `EventType` = `payload.GetType().AssemblyQualifiedName` — always the runtime type, never `typeof(T)`.
     Using `typeof(T)` in a generic call would resolve the open generic or the declared parameter type,
     not the concrete event type, and break deserialization on the processor side.
   - `Payload` = serialized by `IMessageSerializer` (injected via constructor).
   - `messageId` and `occurredOnUtc` are **intrinsic to the event** — passed by the caller (MediatR glue),
     not generated inside the factory. `MessageId` is the end-to-end stable identity and the inbox dedup key
     (`MessageId + ConsumerType`); it must not be regenerated at outbox-write.

4. **Metadata origin split:**
   - **Intrinsic** (from the message/event, passed as parameters): `MessageId`, `OccurredOnUtc`.
   - **Ambient** (from `IExecutionContext`, passed as parameter): `TenantId`, `CorrelationId`, `CausationId`.

5. **TenantId is passed through as-is — the factory does NOT throw on null.**
   `IExecutionContext.TenantId` is `string?`. Messaging must operate without Multitenancy
   (ADR-EXEC-001 inversion). A single-tenant app running without Multitenancy will have
   `TenantId = null`; this is valid. The factory passes `context.TenantId` directly to
   `OutboxMessage.TenantId`. It is the responsibility of the host to populate TenantId when
   multi-tenancy is required — not the factory's responsibility to enforce it.
   The EF Core entity configurations treat TenantId as optional (nullable column, no `IsRequired()`).
   **Pending Abstractions change:** `OutboxMessage.TenantId` and `InboxMessage.TenantId` are currently
   declared as `string` (non-nullable) in the entity classes — a separate PR must change them to
   `string?` and update the XML docs to remove the "never null" constraint.

6. **Ambient metadata stamped exactly once, at outbox-write, BEFORE serialization.**
   The values written to `OutboxMessage` columns MUST equal the values embedded in the serialized
   payload, because the processor re-reads them from the deserialized event downstream. A second
   context read at a later stage would risk drift under concurrent scope changes.

7. **`IExecutionContext` passed as method parameter, not constructor-injected.**
   `OutboxMessageFactory` is registered as `Singleton`. `IExecutionContext` is scoped. Injecting
   a scoped dependency into a singleton creates a captive dependency — the scoped value is captured
   at first resolution and never refreshed. Passing `IExecutionContext` as a method argument keeps
   the factory stateless and singleton-safe.

8. **MediatR-specific glue is out of scope for this decision.**
   Extracting `MessageId`/`OccurredOn` from the MediatR notification and stamping ambient context
   before calling the factory is the responsibility of `DomainEventsDispatcher` in
   `MicroKit.Messaging.MediatR` — a separate package implemented in a later session.

### Consequences
- `OutboxMessageFactory` is a simple sealed singleton with two collaborators: `IMessageSerializer`
  (constructor) and `IExecutionContext` (method parameter).
- No breaking change when an `IOutboxMessageMapper` interface is introduced later (additive).
- The EFCore package has zero serializer dependency — confirmed.
- Processors read `EventType` from the `OutboxMessage` row and use it for runtime deserialization;
  `AssemblyQualifiedName` guarantees the concrete type is resolved correctly.
- TenantId null is valid at the persistence layer; Multitenancy enforcement is a host concern.

---

## ADR-MSG-009 — MediatR.Contracts carve-out for the MediatR glue + notification idempotency

### Status
Proposed (to be ratified at architect review).

### Context
The general Messaging rule (CLAUDE.md rule #14, ADR-MSG-002) is "no MediatR / MediatR.Contracts
anywhere": Messaging transports `IIntegrationEvent`, never `INotification`. The MediatR glue
package `MicroKit.Messaging.MediatR` exists precisely to bridge MicroKit.MediatR domain-event
notifications onto the Messaging outbox. The decided topology is:

- `DomainEventsDispatcher` (glue) drains domain events and writes each **notification** to the
  transactional outbox (payload = serialized `IDomainEventNotification`). It does NOT publish
  synchronously (ADR-MSG-008 §5 rationale: a synchronous publish here plus the outbox republish
  would double-execute every handler).
- `MediatROutboxDispatcher` (glue) is a **routing decorator** over the Core `IOutboxDispatcher`:
  payload `is INotification` → `IPublisher.Publish`; otherwise delegate to the wrapped Core
  dispatcher (`InProcessIntegrationDispatcher`), preserving integration-events-via-outbox
  (ADR-MSG-002).

This requires the glue to reference `MediatR` (`IPublisher`) and, transitively,
`MediatR.Contracts` (`INotification`, since `IDomainEventNotification<out TEvent> : INotification`).

### Decision
1. **Carve-out:** `MicroKit.Messaging.MediatR` is the **only** Messaging package permitted to
   reference `MediatR` / `MediatR.Contracts`. `Abstractions`, `Core`, `EntityFrameworkCore`, and
   `Testing` stay MediatR-free — enforced by ArchitectureTests
   (`AllAssemblies_HaveNoMediatRContractsDependency` includes Abstractions/Core/EfCore and, once it
   exists, Testing; the glue is intentionally excluded; `Core_HasNoMediatRDependency` covers full
   MediatR on Core).
2. **Routing disjointness:** the decorator routes `is INotification` first. This is safe only
   because `IIntegrationEvent` and `IDomainEventNotification` are disjoint — no integration event is
   a notification and vice versa. If that ever changes, the routing order must be revisited.
3. **Notification idempotency (extends ADR-MSG-003 to the outbox→MediatR path):** notification
   handlers have no per-consumer inbox. The whole `IPublisher.Publish` call is the retry unit, so an
   outbox retry re-runs ALL of a notification's handlers. Domain-event handlers reached via the glue
   MUST therefore be idempotent — same contract as inbox handlers, documented in the glue README and
   the `AddMediatRTransport` XML docs.

### Consequences
- The "MediatR.Contracts forbidden everywhere" statements in CLAUDE.md (rule #14), the dependencies
  rule, and the testing rule carry an explicit one-line carve-out referencing this ADR, so the docs
  are not self-contradictory.
- `transaction-behavior-design.md` (v4) is superseded by ADR-MSG-008 §5 (no synchronous in-transaction
  publish) and this ADR; it must not be used to re-introduce a synchronous publish or an
  `IOutboxMessageMapper`.
