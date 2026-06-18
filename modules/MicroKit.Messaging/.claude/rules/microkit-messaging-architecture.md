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
| `MicroKit.MediatR.Messaging` (glue) | planned | `DomainEventsDispatcher`, `MediatorOutboxDispatcher`, `TransactionBehavior`; sources current `TenantId`, passes it explicitly to `IOutboxWriter.AddAsync` |
| `MicroKit.Multitenancy(.Abstractions/.EFCore)` | deferred | `ITenantSource`, tenant-aware `IExecutionScopeFactory` impl |
| `MicroKit.Messaging.Multitenancy` | deferred | PerTenant coordinators (integration, reuses Core `IOutboxProcessor`/`IInboxProcessor`) |
