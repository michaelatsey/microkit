# Context: Architectural Decisions

**ADR (Architecture Decision Records) for MicroKit.MediatR.**

Format: `## ADR-{NNN}: {Title}` · Status: `Accepted` | `Proposed` | `Superseded` | `Deprecated`

---

## ADR-001: MicroKit.MediatR.Abstractions Depends on MicroKit.Result

**Status:** Accepted  
**Date:** 2026-05-28  

### Decision

`MicroKit.MediatR.Abstractions` takes a **production dependency** on `MicroKit.Result`. The CQRS
contracts (`ICommand<TResult>`, `IQuery<TResult>`) are routinely closed over `Result<T>`, and the
built-in behaviors produce `Result.Failure(...)` when `TResponse` is a `Result<T>`.

### Rationale

1. **The contracts must express `Result<T>`** — `ICommand<Result<OrderId>>` is the canonical happy
   path. Without the dependency, every consumer would re-declare a bridge type.
2. **Behaviors need to construct failures** — `ValidationBehavior` and `AuthorizationBehavior` return
   `Result.Failure(...)` for `Result<T>` responses. The failure-construction surface lives in `MicroKit.Result`.
3. **The monorepo graph permits it** — MicroKit.MediatR is a Level 2 module; the graph allows
   `MediatR → Result, Domain`. This is the **opposite** of MicroKit.Logging's ADR-006 (which forbids
   a Result dependency) because Logging's enricher contract returns `void` while MediatR's contracts
   are result-bearing.

### Consequences

- `MicroKit.Result` is the only non-`*.Abstractions` MicroKit package that Abstractions may reference.
- Handlers that never fail can still return `T` directly — `Result<T>` is not forced.
- The `dependency-guardian` allowlists `MicroKit.Result` for Abstractions and blocks every other
  concrete MicroKit package.

---

## ADR-002: BehaviorBase Is Mandatory

**Status:** Accepted  
**Date:** 2026-05-28  

### Decision

All pipeline behaviors inherit `BehaviorBase<TRequest, TResponse>` (in core) rather than implementing
MediatR's `IPipelineBehavior<TRequest, TResponse>` directly.

### Rationale

- `BehaviorBase` provides the `Order` property that ties a behavior to the `PipelineOrder` registry,
  making ordering explicit and reviewable.
- It centralizes helpers for `Result<T>` vs `T` response detection and failure construction, so each
  behavior does not re-implement reflection-heavy plumbing.
- It gives the `behavior-designer` agent and the `architecture-check` hook a single base type to
  validate against.

### Consequences

- A behavior implementing `IPipelineBehavior` directly is a violation flagged at review.
- The response-type detection is implemented once in `BehaviorBase` and cached per closed generic
  (supports ADR-003's performance goal).

---

## ADR-003: ValueTask over Task on Handlers

**Status:** Accepted  
**Date:** 2026-05-28  

### Decision

Command and query handlers return `ValueTask`/`ValueTask<T>`. Notification handlers return `Task`
(MediatR's `INotificationHandler` contract). Stream handlers return `IAsyncEnumerable<T>`.

### Rationale

- Many handlers complete synchronously (cache hit, in-memory projection, guard-clause failure).
  `ValueTask` avoids allocating a `Task` state-machine box on that fast path.
- The pipeline is on every request — eliminating one allocation per dispatch is material at scale.
- Notification handlers cannot change return type without breaking MediatR's contract, so they remain `Task`.

### Consequences

- A Command/Query handler returning `Task<T>` is flagged by `architecture-check` and `performance-check`.
- The performance budget assumes the synchronous path is zero-allocation (see
  `.claude-context/standards/performance-budget.md`).

---

## ADR-004: Behaviors Are Opt-In via Markers (Logging Excepted)

**Status:** Accepted  
**Date:** 2026-05-28  

### Decision

Every behavior except `LoggingBehavior` activates only when the request implements its marker
interface (`IAuthorizedRequest`, `IIdempotentCommand`, `ICacheableQuery`, `IRetryableRequest`) or, for
validation, has a registered `IValidator<T>`. `LoggingBehavior` is always active.

### Rationale

- Imposing caching, retry, or idempotency on every request without consent is surprising and unsafe
  (e.g., caching a command's side effects).
- The marker is a visible, type-checked declaration at the call site — the consumer opts in deliberately.
- Logging is the exception because universal observability has no downside and no risk of incorrect application.

### Consequences

- The first statement of every opt-in behavior is the marker guard (zero-cost pass-through).
- The marker suffix encodes scope (`*Command` ⇒ commands only, `*Query` ⇒ queries only).
- `/audit-pipeline` flags a command marked `ICacheableQuery` or a query marked `IIdempotentCommand`.

---

## ADR-005: Registration-Time Scan for Notification Mapping and Handler Dispatch Map

**Status:** Accepted (partially superseded by ADR-MEDIATR-009 — see below)
**Date:** 2026-05-28 | **Revised:** 2026-06-21

### Decision

`AddMicroKitMediatR` performs two independent registration-time scans over the provided assemblies:

- **Phase A — handler scan:** discovers `IDomainEventHandler<TEvent>` (single type parameter,
  per ADR-MEDIATR-009) implementations and compiles a
  `Func<IServiceProvider, IDomainEvent, CancellationToken, Task>` delegate per concrete handler
  type. Delegates are collected into a `HandlerDispatchMap` singleton. At dispatch time,
  `IDomainEventHandlerDispatcher.DispatchAsync(IDomainEvent)` looks up the runtime event type
  in O(1) and invokes the pre-compiled delegates — zero reflection per dispatch.

- **Phase B — notification scan:** discovers `DomainEventNotification<TEvent>` subclasses and
  compiles a `Func<IDomainEvent, INotification>` factory per event type. This map is registered
  as an `IDomainEventNotificationFactory` singleton. At dispatch time, `Create(IDomainEvent)`
  returns the wrapped notification or `null` when the event type has no registered notification.

The two scans are **independent**: a domain event can have handlers without a notification
(pure in-transaction business effects) and can have a notification without handlers (pure
outbox fan-out path). Both, neither, or one-of-the-two are valid configurations.

### What superseded the original ADR-005 (ADR-MEDIATR-009)

The original ADR-005 described `IDomainEventHandler<TEvent, TNotification>` (two type params),
`IDomainEventDispatcher.PublishAsync(IEvent)`, and a single scan that derived the notification
type from the handler declaration. ADR-MEDIATR-009 replaced this with single-parameter handlers
and separate scans. The **registration-time scan principle and the O(1) dispatch lookup** are
unchanged and still govern the design; only the scan structure and interface shapes changed.

### Rationale

1. **AOT/trimming safety:** `AppDomain.CurrentDomain.GetAssemblies()` scanned at first publish
   is incompatible with .NET NativeAOT and the trimmer. Registration-time scanning uses the
   assemblies explicitly provided by the consumer via `MediatRBuilder.FromAssembly(...)`, which
   are already known to the trimmer.
2. **Startup conflict detection:** If two `DomainEventNotification<TEvent>` subclasses exist for
   the same event type, `AddMicroKitMediatR` throws at DI startup with a clear error — the
   1-event → 1-notification constraint (Phase B) is verified eagerly.
3. **O(1) dispatch lookup:** After startup, all dispatch paths perform one dictionary lookup and
   invoke the pre-compiled delegate — zero per-dispatch reflection.
4. **Captive dependency safety:** `HandlerDispatchMap` is a singleton (compiled delegates only).
   `DomainEventHandlerDispatcher` is scoped and resolves handlers from its scope's
   `IServiceProvider`, avoiding the singleton-wrapping-scoped-service captive dependency problem.

### Consequences

- Consumers must pass all assemblies containing domain event handlers and notification classes
  to `MediatRBuilder.FromAssembly` or `FromAssemblyContaining<T>`. Missing assemblies are not
  detected at DI startup; they surface only at first dispatch (handler silently absent, or
  notification factory returns `null`).
- **1-event → 1-notification constraint (Phase B):** each `IDomainEvent` type maps to at most
  one `DomainEventNotification<TEvent>` subclass. Multiple `INotificationHandler<TNotification>`
  implementations provide fan-out via MediatR for a single notification type.
- `IDomainEventNotificationFactory` (internal singleton) is not a service locator — it is a
  pre-compiled factory dictionary injected via DI.
- `IDomainEventHandler<TEvent>` has **no** notification type parameter. The notification
  mapping is purely a Phase B concern; handler authors do not declare it.

---

## ADR-006: AuthorizationBehavior Uses `ICurrentUserAccessor`, Not `IHttpContextAccessor`

**Status:** Accepted
**Date:** 2026-05-29

### Decision

`AuthorizationBehavior` injects `ICurrentUserAccessor` (defined in `MicroKit.MediatR.Abstractions`)
to obtain the current `ClaimsPrincipal`, rather than injecting `IHttpContextAccessor` directly.

### Rationale

1. **Non-web host compatibility:** `IHttpContextAccessor.HttpContext` returns `null` outside an
   ASP.NET Core HTTP request. Worker services, message consumers, and background tasks — all
   first-class MicroKit.MediatR use cases — would NullReferenceException silently.
2. **Decoupling principle:** The dispatch pipeline must not be coupled to the HTTP runtime.
   This mirrors the rule in `no-handler-coupling.md` that forbids `HttpContext` in handlers.
3. **BCL-only contract:** `ClaimsPrincipal` is in `System.Security.Claims` (BCL) — no extra
   package required in `Abstractions`.

### Consequences

- `ICurrentUserAccessor` is added to `MicroKit.MediatR.Abstractions`. Consumers must register
  an implementation: ASP.NET Core apps use `HttpContextCurrentUserAccessor` (ships in Behaviors);
  non-HTTP hosts implement their own.
- `Microsoft.AspNetCore.Authorization` (`IAuthorizationService`) remains in `Behaviors` — the
  web-framework dependency is reduced but not eliminated. A future `MicroKit.MediatR.Behaviors.Authorization`
  split-package is the clean exit for non-ASP.NET Core consumers (tracked as v2 debt).

---

## ADR-007: Idempotency and Caching Behaviors Require Explicit `ResultJsonConverterFactory` Registration

**Status:** Accepted
**Date:** 2026-05-29

### Decision

`IdempotencyBehavior` and `CachingBehavior` consume `IOptions<JsonSerializerOptions>` from DI for
serialization. When `TResponse` is `Result<T>`, consumers must register `ResultJsonConverterFactory`
via `services.Configure<JsonSerializerOptions>(opts => opts.Converters.Add(new ResultJsonConverterFactory()))`.

### Rationale

1. **`Result<T>` is not default-serializable:** `Result<T>` is a `readonly struct` with private
   constructors and an internal `byte _tag` field. `JsonSerializerOptions.Default` (no custom
   converters) would serialize to `{}` and deserialize to `default(Result<T>)` — an uninitialised
   struct that throws on any value access.
2. **`ResultJsonConverterFactory` exists:** Confirmed present in `MicroKit.Result/Serialization/`.
   It provides correct round-trip semantics for `Result<T>`.
3. **`IOptions<T>` pattern:** The standard .NET pattern for configurable infrastructure. Consumers
   configure once via `IOptions<JsonSerializerOptions>`; behaviors consume it at dispatch time.

### Consequences

- Consumers using Idempotency or Caching with `Result<T>` responses must call
  `services.Configure<JsonSerializerOptions>(...)` once during DI setup.
- Behaviors log a WARNING (not throw) when `TResponse` is `Result<T>` and the factory is absent,
  to avoid hard startup failures for consumers who handle non-Result responses.
- `IOptions<JsonSerializerOptions>` is injected into both `IdempotencyBehavior` and `CachingBehavior`
  constructors as a required dependency.

---

## ADR-008: `ICurrentUserAccessor` Lives in `MicroKit.MediatR.Abstractions` (v1 Pragmatic Placement)

**Status:** Accepted  
**Date:** 2026-05-29

### Decision

`ICurrentUserAccessor` is declared in `MicroKit.MediatR.Abstractions` for v1. It is a candidate
for promotion to a future `MicroKit.Abstractions` (or `MicroKit.Identity.Abstractions`) package
once cross-module identity contracts are centralized in the monorepo.

### Context

`AuthorizationBehavior` needs to obtain the current `ClaimsPrincipal` without coupling to
`IHttpContextAccessor` (see ADR-006). The interface uses only BCL types (`System.Security.Claims`),
so it has no package dependency beyond the BCL — it could live anywhere in the graph without
creating a new edge.

The question is: which package *owns* the contract?

### Rationale for current placement (`MicroKit.MediatR.Abstractions`)

1. **Single consumer today.** Only `AuthorizationBehavior` (in `MicroKit.MediatR.Behaviors`) reads
   `ICurrentUserAccessor`. Placing it in Abstractions keeps the interface collocated with the
   contract it serves. No other module needs it today.
2. **Avoids a premature shared package.** Creating `MicroKit.Abstractions` or
   `MicroKit.Identity.Abstractions` for a single interface would introduce monorepo infrastructure
   (new project, new NuGet ID, new CI step, new versioning) with no immediate payoff. The
   organizational cost exceeds the benefit until at least two modules share the interface.
3. **Zero extra dependency edge.** `MicroKit.MediatR.Abstractions` is already the lowest-level
   MediatR package. Placing `ICurrentUserAccessor` here adds no new edge to the dependency graph.
4. **BCL-only surface.** `ClaimsPrincipal` is in `System.Security.Claims` (BCL). The interface
   does not pull any additional NuGet package into `Abstractions`.

### Why `MicroKit.Abstractions` is the right long-term home

1. **Cross-module identity contracts will emerge.** `MicroKit.Auth`, `MicroKit.Multitenancy`, and
   `MicroKit.Persistence` (audit columns, row-level security) will all need a stable identity
   contract. If each module declares its own `ICurrentUserAccessor`, consumers end up with
   three incompatible interfaces and three independent registration points for the same concept.
2. **`MicroKit.Abstractions` is the canonical Level 0 anchor.** It has no MicroKit dependencies
   and can be referenced by any module without creating a cycle. That makes it the correct owner
   of primitives that span the ecosystem.
3. **Prevents interface duplication.** A centralized package ensures every module that needs
   "who is the current user?" answers it with the same type, enabling one registration at DI startup.

### Affected modules (current and projected)

| Module | Current use | Projected use |
|--------|------------|---------------|
| `MicroKit.MediatR` | `AuthorizationBehavior` reads `ICurrentUserAccessor` | unchanged |
| `MicroKit.Auth` | — | will need principal for policy evaluation |
| `MicroKit.Persistence` | — | will need principal for audit columns / row-level security |
| `MicroKit.Multitenancy` | — | will need principal for tenant resolution |

### Migration path (when `MicroKit.Abstractions` is introduced)

1. **Create `MicroKit.Abstractions`** as a new Level 0 project with its own package ID and
   `version.json`. No MicroKit dependencies; BCL only.
2. **Move `ICurrentUserAccessor`** into `MicroKit.Abstractions` under the
   `MicroKit.Abstractions` namespace (or a sub-namespace such as `MicroKit.Abstractions.Identity`).
3. **Add `MicroKit.Abstractions` as a dependency** of `MicroKit.MediatR.Abstractions` (allowed —
   both are Level 0).
4. **Provide a type alias in `MicroKit.MediatR.Abstractions`** for one release cycle to avoid a
   hard breaking change for consumers who reference the old namespace directly:
   ```csharp
   // MicroKit.MediatR.Abstractions — compatibility shim, deprecated in v2
   [Obsolete("Use MicroKit.Abstractions.ICurrentUserAccessor. This alias will be removed in v2.")]
   global using ICurrentUserAccessor = MicroKit.Abstractions.ICurrentUserAccessor;
   ```
5. **Update `MicroKit.MediatR.Behaviors`** to import from the new namespace — no behavior
   logic changes; only the `using` directive changes.
6. **Remove the shim** in the next major version (`mediatr-v2.0.0`).

### Trigger condition for promotion

Promotion should be initiated when **any of the following is true**:
- A second module (e.g., `MicroKit.Auth` or `MicroKit.Persistence`) needs to reference
  `ICurrentUserAccessor` and would otherwise take a dependency on `MicroKit.MediatR.Abstractions`
  purely for this interface.
- A `MicroKit.Abstractions` package is created for any other cross-cutting primitive, making
  the migration cost near-zero.

Until the trigger fires, the current placement is correct. Do not promote prematurely.

### Consequences

- **v1 consumers** register `ICurrentUserAccessor` against a type in `MicroKit.MediatR` namespace.
  Their registration code will need a one-line namespace update at v2.
- **`dependency-guardian`** must allowlist `MicroKit.MediatR.Abstractions → MicroKit.Abstractions`
  when the promotion occurs (new edge in the dependency graph, requires graph update in
  `modules/MicroKit.MediatR/.claude-context/context/dependency-graph.md` and the root
  `.claude/CLAUDE.md` module boundaries section).
- **No action required today.** This ADR exists to document intent and prevent ad-hoc duplication
  of the interface in other modules before the centralized package is ready.

---

## ADR-MEDIATR-009: IDomainEventHandler Redesigned to Single Type Parameter

**Status:** Accepted  
**Date:** 2026-06-21  

### Decision

`IDomainEventHandler<TEvent, TNotification>` (two type parameters) is replaced by
`IDomainEventHandler<TEvent>` (single type parameter). Handlers receive the raw domain event
directly rather than a MediatR notification wrapper. A new `IDomainEventHandlerDispatcher`
interface (scoped) enables direct, synchronous handler dispatch without MediatR's notification
pipeline.

### Context

The two-parameter signature forced handler authors to know MicroKit MediatR internals
(`DomainEventNotification<TEvent>`) and coupled the handler contract to the MediatR transport
layer. This made it impossible to invoke handlers directly (e.g., synchronously within a
transaction before the outbox write) without going through MediatR's `IPublisher`, which would
require instantiating a notification wrapper at each call site. The constraint was explicitly
blocking the `MicroKit.Messaging.MediatR` glue layer's P3 phase (synchronous handler dispatch
inside the persistence transaction).

### Rationale

1. **Handler authors must not know MediatR internals.** The notification type is an infrastructure
   concern. The only contract a handler needs is: "handle this domain event".
2. **Direct dispatch without MediatR's pipeline.** `IDomainEventHandlerDispatcher.DispatchAsync`
   invokes `IDomainEventHandler<TEvent>.Handle` directly via pre-compiled Expression tree delegates
   built at DI startup. No `IPublisher.Publish`, no reflection per-dispatch.
3. **Captive dependency safety.** `HandlerDispatchMap` (singleton) holds the compiled delegates.
   `DomainEventHandlerDispatcher` (scoped) resolves handlers from its scope's `IServiceProvider`.
   This split avoids the singleton-wrapping-scoped-service captive dependency problem.
4. **Phase B scan remains independent.** `DomainEventNotification<TEvent>` subclasses are still
   scanned at startup to build the `INotificationFactory` map for the MediatR fan-out path
   (P4 / outbox). This scan is now separate from the handler scan — handler types no longer
   declare their notification type.

### The dispatch phases post-ADR-MEDIATR-009

`DomainEventDispatchBehavior` (order 50, outermost MediatR pipeline behavior) calls
`IDomainEventsDispatcher.DispatchEventsAsync` after the inner pipeline and handler complete.
That call runs four sequential phases:

| Phase | Name | Mechanism | Use |
|-------|------|-----------|-----|
| **P1** | Drain | `IDomainEventsProvider.DrainDomainEvents()` | Collect events accumulated on aggregates |
| **P2** | Handler dispatch | `IDomainEventHandlerDispatcher.DispatchAsync` → `IDomainEventHandler<TEvent>` | Sync, in-transaction, raw event; business effects |
| **P3** | Notification creation | `IDomainEventNotificationFactory.Create()` → `IDomainEventNotification<TEvent>` | Build the outbox payload wrapper (null if no mapping) |
| **P4** | Outbox batch write | `IOutboxWriter.AddBatchAsync()` | Stage all notifications in one DB round-trip, same transaction |

The two handler types are **structurally disjoint**:
- P2: `IDomainEventHandler<TEvent>` — receives the raw domain event, runs in-transaction.
- Post-commit: `INotificationHandler<TNotification>` — receives the notification, runs via outbox processor after commit.

An event can participate in both P2 and P3/P4, one, or neither. ADR-MEDIATR-005 still applies to P3: exactly one notification type per event type.

### Breaking change

- **`IDomainEventHandler<TEvent, TNotification>`** → **`IDomainEventHandler<TEvent>`** in
  `MicroKit.MediatR.Abstractions`. All implementing classes must drop the second type parameter
  and change `Handle(TNotification notification, ...)` to `Handle(TEvent domainEvent, ...)`.
- **`DomainEventTestHarness<TEvent, TNotification>`** → **`DomainEventTestHarness<TEvent>`** in
  `MicroKit.MediatR.Testing`. All test usages must drop the second type parameter and pass
  a raw domain event to `HandleAsync(TEvent domainEvent, ...)`.
- Package version bump: `1.0.0-preview.1` → `1.0.0-preview.2`.
- `DomainEventHandlerAdapter` (internal) is deleted — the adapter pattern is no longer needed.

### Consequences

- Handler registrations remain scoped (unchanged).
- `IDomainEventDispatcher` is now scoped (was previously resolved through a transient adapter).
  The `DomainEventDispatcher` implementation drains `IDomainEventsProvider` and delegates to
  `IDomainEventHandlerDispatcher`, keeping the pipeline integration point unchanged.
- Consumers who only use `INotificationHandler<TNotification>` (pure MediatR fan-out) are
  unaffected — notification classes and `DomainEventNotification<TEvent>` are unchanged.
- The `dependency-guardian` must verify that no module depends on `DomainEventHandlerAdapter`
  (deleted) and that all `IDomainEventHandler<,>` (two-param) usages are migrated.
- `IDomainEventsDispatcher` is now the canonical interface name (plural). The singular
  `IDomainEventDispatcher` is a preview-only compatibility alias marked `[Obsolete]`
  and will be removed in the next major version. Both resolve to the same scoped
  `DomainEventDispatcher` implementation in DI.
- `DomainEventDispatchBehavior<TRequest, TResponse>` (in `MicroKit.Messaging.MediatR`) is the
  pipeline behavior (order 50) that calls `IDomainEventsDispatcher.DispatchEventsAsync` after
  the command handler. Registered via `AddMediatRTransport()`. See ADR-MSG-010.

---

## ADR-MEDIATR-010: Event Taxonomy and Dispatch Topology

**Status:** Accepted  
**Date:** 2026-06-21

### Decision

MicroKit adopts a **single canonical event root** (`MicroKit.Domain.Events.IEvent`) and a
**three-tier taxonomy** of event contracts:

| Contract | Location | Semantics |
|----------|----------|-----------|
| `MicroKit.Domain.Events.IEvent` | `MicroKit.Domain` | Root marker — all MicroKit event types |
| `IDomainEvent : IEvent` | `MicroKit.Domain` | Aggregate fact — has `EventId` + `OccurredAt` |
| `IIntegrationEvent : IEvent` | `MicroKit.Messaging.Abstractions` | Cross-service message — standalone, not a domain fact |

> **NOTE:** `IApplicationEvent` was considered but **rejected — YAGNI** (root `.claude/CLAUDE.md` global conventions prohibit introducing this until a concrete use case exists in the monorepo). It is not implemented and must not be added to `MicroKit.MediatR.Abstractions` without revisiting this decision.

`MicroKit.MediatR.Events.IEvent` that previously existed as a local event root is **superseded**
and is now a `[Obsolete]` shim that extends `MicroKit.Domain.Events.IEvent`.

### Dispatch topology — full phase sequence

`DomainEventDispatchBehavior` (order 50) is the MediatR pipeline behavior that triggers dispatch.
After the command handler and all inner behaviors complete, it calls
`IDomainEventsDispatcher.DispatchEventsAsync`, which runs:

```
Command arrives → [DomainEventDispatchBehavior wraps everything]
  → [Logging → Auth → Validation → … → Handler]
  → DispatchEventsAsync():
      P1  DrainDomainEvents()          — collect events from aggregates
      P2  IDomainEventHandler<TEvent>  — sync · in-transaction · business effects
      P3  IDomainEventNotificationFactory.Create() — build IDomainEventNotification<TEvent>
      P4  IOutboxWriter.AddBatchAsync() — stage all notifications · one DB round-trip
  → IUnitOfWork.CommitAsync()          — commits domain changes + outbox rows atomically
```

| Phase | Interface | Invocation | Guarantee |
|-------|-----------|-----------|-----------|
| **P2** | `IDomainEventHandler<TEvent>` | Sync, in-transaction, raw event | At-most-once per commit; fails = rollback |
| **P3** | `IDomainEventNotificationFactory` | In-transaction, builds wrapper | Null if no mapping registered |
| **P4** | `IOutboxWriter.AddBatchAsync` | In-transaction, staged in EF change tracker | Committed atomically with domain changes |
| Post-commit | `INotificationHandler<TNotification>` | Via outbox processor, after commit | At-least-once; handler MUST be idempotent |

The two handler types are **structurally disjoint**:
- P2 (`IDomainEventHandler<TEvent>`) receives the raw domain event — runs in-transaction via `IDomainEventHandlerDispatcher`, not via MediatR.
- Post-commit (`INotificationHandler<TNotification>`) receives the notification wrapper — published by `IPublisher.Publish` after the outbox processor dequeues the `DomainEventNotification<TEvent>` payload.
- An event can participate in both, one, or neither path simultaneously.

### Rationale

1. **Single event root eliminates ambiguity.** Before this ADR, `MicroKit.MediatR` defined its
   own `IEvent`, creating a parallel hierarchy that conflicted with `MicroKit.Domain.Events.IEvent`.
   A single root (`Domain.Events.IEvent`) makes the taxonomy derivable from one source of truth.
2. **`IIntegrationEvent` is deliberately NOT a `IDomainEvent`.** Integration events cross service
   boundaries and are transport artifacts, not domain facts. Inheriting `IDomainEvent` would impose
   `EventId`/`OccurredAt` semantics and couple the messaging contract to the domain model (ADR-MSG-001).
3. **Two disjoint pipelines are better than one combined pipeline.** The P-handler path gives
   teams a simple way to run business effects in-transaction (e.g., denormalize into a read model
   synchronously). The P-notification path provides reliable asynchronous fan-out. Merging them
   would force all handlers to be idempotent (outbox semantics), or would eliminate the
   in-transaction guarantee (notification semantics).
4. **Handler authors must not know the notification type.** `IDomainEventHandler<TEvent>`
   (single param) keeps the business handler decoupled from the MediatR/outbox plumbing.
   The notification wrapper is an infrastructure artifact declared separately.

### Consequences

- All domain-event dispatch contracts (`IDomainEventHandler<TEvent>`,
  `IDomainEventHandlerDispatcher.DispatchAsync`, `DomainEventNotification<TEvent>`,
  `IDomainEventNotification<TEvent>`) are constrained to `where TEvent : IDomainEvent`.
  Code targeting the shim `MicroKit.MediatR.Events.IEvent` must migrate to
  `MicroKit.Domain.Events.IEvent`.
- `MicroKit.MediatR.Events.IEvent` is `[Obsolete]` — it will be removed in the next major version.
- `IDomainEventsDispatcher` (plural) is the canonical name. `IDomainEventDispatcher` (singular)
  is a `[Obsolete]` alias and will be removed in the next major version.
- `IDomainEventNotificationFactory` (in Abstractions) supersedes the former `INotificationFactory`,
  which is now a `[Obsolete]` alias.
- P-notification handlers MUST be idempotent — the outbox retry re-runs ALL notification handlers
  for the message (ADR-MSG-003, ADR-MSG-009).

---

## ADR-MEDIATR-011: MicroKit.MediatR.Behaviors Takes a Dependency on MicroKit.Persistence.Abstractions

**Status:** Accepted  
**Date:** 2026-06-22

### Decision

`MicroKit.MediatR.Behaviors` takes a production dependency on `MicroKit.Persistence.Abstractions`
to access `ITransactionalContext`. This contract is required by
`TransactionBehavior<TRequest, TResponse>` (pipeline order 700) to open, commit, and roll back
database transactions around command handlers and their domain-event dispatch.

### Rationale

1. **`TransactionBehavior` needs a transaction abstraction.** Without one, every consumer would
   re-declare their own unit-of-work wrapping behavior. Shipping it in `MicroKit.MediatR.Behaviors`
   provides it once, with the correct order (700) and the correct integration point for
   `IDomainEventsDispatcher.DispatchEventsAsync` within the transaction boundary.

2. **Dependency is on the Abstractions layer only — not EF Core.** `TransactionBehavior` injects
   `ITransactionalContext` (the interface). It has no knowledge of EF Core, Dapper, or any concrete
   ORM. The implementation is provided by the consumer at DI registration time (e.g.,
   `MicroKit.Persistence.EntityFrameworkCore` via `AddEntityFrameworkCore()`).

3. **No circular dependency.** `MicroKit.Persistence.Abstractions` does not depend on
   `MicroKit.MediatR`. Both packages are at Level 2 in the monorepo graph. The dependency is
   uni-directional: `Behaviors → Persistence.Abstractions`.

4. **Consistent with ADR-001.** `MicroKit.MediatR.Abstractions` already takes a deliberate
   cross-module dependency on `MicroKit.Result` for an analogous reason: a shipped contract
   requires a peer abstraction, and the dependency creates no cycle.

5. **Opt-in registration.** `AddTransactionBehavior()` is the only DI entry point.
   Consumers who do not call it pay zero cost — `ITransactionalContext` is not resolved.

### Consequences

- The root `.claude/CLAUDE.md` dependency graph for `MicroKit.MediatR` is updated to include
  `Persistence.Abstractions` in the allowed-dependency list.
- The module `.claude/CLAUDE.md` dependency table for `MicroKit.MediatR.Behaviors` is updated.
- The `MicroKit.MediatR.Behaviors.csproj` CIReleaseBuild two-ItemGroup pattern already declares
  `MicroKit.Persistence.Abstractions` in both groups (compliant with cross-module-references rule).
- Consumers who call `AddTransactionBehavior()` without registering `ITransactionalContext` will
  get a clear DI resolution failure at startup — not a runtime `NullReferenceException`.
- The `dependency-guardian` allowlist for `MicroKit.MediatR.Behaviors` is updated: `MicroKit.Persistence.Abstractions` is now permitted.

---

## ADR-MEDIATR-012: `TransactionBehavior` Discards Inside the `ExecuteAsync` Operation, via Catch/Rethrow

**Status:** Accepted
**Date:** 2026-08-20
**Related:** ADR-MEDIATR-011 (Behaviors → Persistence.Abstractions), ADR-002 (BehaviorBase),
ADR-005 in `MicroKit.Persistence` (`IUnitOfWork.DiscardChanges`), PR #78 (flush inside the transaction),
PR #80 (the member ships)

### Decision

`TransactionBehavior<TRequest, TResponse>` is the caller of `IUnitOfWork.DiscardChanges()`. It calls it
on **every non-commit exit** of a command boundary — business failure *and* thrown exception — from
**inside** the `ITransactionalContext.ExecuteAsync` operation, structured as catch-and-rethrow with the
discard itself guarded:

```csharp
try
{
    var response = await state.Next().ConfigureAwait(false);

    if (ResultInspector<TResponse>.IsFailure(response))
    {
        state.UnitOfWork.DiscardChanges();       // business failure
        return response;
    }

    await state.Dispatcher.DispatchEventsAsync(ct).ConfigureAwait(false);
    await state.UnitOfWork.CommitAsync(ct).ConfigureAwait(false);
    return response;
}
catch
{
    try { state.UnitOfWork.DiscardChanges(); }
    catch { /* never mask the in-flight exception */ }
    throw;                                        // bare throw — original stack preserved
}
```

ADR-005 (Persistence) fixed the *contract* and the *requirement* — every non-commit exit discards —
and explicitly deferred the call site: "Placement of the call is a `MicroKit.MediatR` decision, not
this one." This ADR is that decision. No constructor change was needed: `IUnitOfWork` has been
injected since #78.

### Rationale

**1. The behavior owns both exits, never the handler.** A command handler cannot know whether its
scope holds one command or twenty; the behavior is the only place that knows a command boundary just
ended. The behavior already owns the *commit* decision — splitting ownership of a boundary between
behavior and handler is exactly how the #78 defect arose. A handler that calls `DiscardChanges()` is
a defect, not a style choice (see Consequences).

**2. Both exits, because a rollback does not reset the change tracker.** EF Core's transaction
rollback undoes what was written; the entities the handler staged stay `Added`/`Modified`, exactly as
they do after a business failure. `DbContext` is scoped, not per-command, so in any scope that
outlives one command the next `SaveChangesAsync` writes them. A fix scoped to `Result.IsFailure`
would be half a fix — and for the persistence layer the thrown path (`PersistenceException`,
`DbUpdateConcurrencyException`, handler exceptions) is the *more* common failure mode.

**3. Inside the operation, not around it.** The discard sits inside the `ExecuteAsync` lambda, so it
lands on the retry-attempt boundary of a provider execution strategy: each attempt begins from a
clean change set (ADR-005 Consequences; compatible with the deferred DN-001 retry work). Wrapping
`ExecuteAsync` from outside would discard once per *command*, not once per *attempt*.

**4. `catch`, not `finally`.** The discard must run only on a non-commit exit. A `finally` would also
run after a successful commit and would need a "did I commit?" flag to suppress itself — mutable
state threaded through a `readonly struct` carrier, and two code paths where there is one. Catch and
rethrow is the shape Microsoft's own exception guidance gives for compensation that must happen only
on failure (rollback in the `catch`, then rethrow); `finally` is for cleanup that must happen
unconditionally. This is cleanup that must happen *conditionally*.

**5. Bare `throw`, never `throw ex`.** `throw ex` resets the stack trace to the rethrow point and
destroys the origin of the failure. The bare rethrow preserves both the instance and its stack; a
unit test pins this (`ShouldBeSameAs` plus a stack-trace assertion), because the difference is
invisible at a glance in review.

**6. `DiscardChanges` is synchronous `void` by design, and that is what makes this safe.** No `await`
inside a `catch` block means no risk of losing or reordering the in-flight exception, and no
state-machine work on the failure path (ADR-005 Rationale §7).

**7. The zero-allocation pattern is untouched.** `Handle` stays non-`async`, the state carrier stays a
`private readonly struct`, the lambda stays `static`, and the try/catch lives inside the lambda —
which only commands reach. The pass-through `return next()` for queries, events, and non-command
requests is unchanged and still allocates nothing. A try/catch costs nothing on the non-throwing path.

### Why the inner `try`/`catch` around the discard is not defensive noise

This is the line a future contributor is most likely to "simplify" away. It is recorded here so that
the ADR, not the reviewer's memory, is what stops them.

It is the documented remedy for a specific failure mode: **an exception thrown by cleanup inside a
`catch` block discards the in-flight exception and surfaces an unexpected one to the outer handler.**
Without the guard, an `IUnitOfWork` implementation whose `DiscardChanges()` throws would replace the
real failure — the handler exception, the concurrency conflict, the constraint violation — with an
unrelated discard exception. The original failure is not chained, not logged, and not recoverable: it
simply never surfaces. The diagnosis is destroyed at precisely the moment it is needed most.

It is unreachable on EF Core: `ChangeTracker.Clear()` cannot fail. That is not the relevant question.
`IUnitOfWork` is provider-agnostic **by construction** — ADR-005 §6 admits Dapper, Marten, NHibernate
and in-memory implementers, and `TransactionBehavior` deliberately has no EF Core reference
(ADR-MEDIATR-011), so it can never know which implementation DI has bound. Three lines guard against
a class of failure that, when it happens, is silent by definition.

**Deletion criterion, stated so it can be checked rather than argued:** this guard is removable only
if `IUnitOfWork` stops being provider-agnostic — i.e. never, while ADR-005 §6 and ADR-MEDIATR-011
stand. "It cannot throw on EF Core" is not sufficient grounds.

### Alternatives Rejected

| # | Alternative | Why it loses |
|---|---|---|
| **A** | `finally` with a `committed` flag | Two paths and mutable state where there is one path and none; the flag must live in or beside a `readonly struct` carrier. The flag exists solely to re-derive "did we take a non-commit exit?" — which the `catch` already knows. |
| **B** | Discard only on `Result.IsFailure` | The half-fix ADR-005 §2 names explicitly. Leaves every thrown `PersistenceException`, `DbUpdateConcurrencyException` and handler exception with a loaded change tracker. A dedicated mutation run (M2 below) exists to keep this from creeping back. |
| **C** | Wrap `ExecuteAsync` from the outside | Discards once per command instead of once per execution-strategy attempt; a retried attempt would start from the previous attempt's change set. |
| **D** | `throw ex` instead of bare `throw` | Resets the stack trace to the behavior, hiding the throwing frame in the handler. |
| **E** | Let `DiscardChanges` throw out of the `catch` | Silently swaps the in-flight exception for an unrelated one — see the section above. |
| **F** | Log the swallowed discard exception | `TransactionBehavior` takes no `ILogger` and adding one for an unreachable-on-EF path would put a dependency on the command hot path to serve a case no shipped provider can reach. `LoggingBehavior` (order 100) already observes the real exception as it propagates. |

### Consequences

- **`TransactionBehavior` is now the sole owner of both exits of the unit of work.** A command handler
  calling `DiscardChanges()` is a defect: it would abandon the staged work of every other command
  sharing the scope. Candidate analyzer **MKP006** ("handlers never call `DiscardChanges`") is the
  enforcement path — **not implemented here**, tracked as follow-up.
- **`DiscardChanges()` can be called twice in one pathological case.** If `DiscardChanges()` itself
  throws on the *business-failure* path, that exception is caught by the outer `catch`, which calls
  the (guarded) discard a second time before rethrowing. Unreachable on EF Core, harmless where
  reachable — the operation is idempotent — and strictly better than leaving the failure path
  unguarded. Recorded rather than left to be discovered.
- **A business failure still commits an empty database transaction.** Nothing threw, so
  `ITransactionalContext` reaches its commit. That was true before this change and remains true; what
  changes is that the change tracker no longer survives it.
- **The `TransactionBehavior` XML documentation states both non-commit exits**, why the exception path
  needs the discard despite the rollback, and — per ADR-005 Consequences — that nested command
  dispatch is unsupported (an inner command's failure would discard the outer command's staged work).
- **No public API change and no `.csproj` change.** Constructor, package graph, and
  `Directory.Packages.props` are untouched; `version.json` is not touched by this decision.
- **The test suite is mutation-verified.** Three mutants are recorded as the sensitivity standard for
  future edits: **M1** — remove both call sites → 10 of 16 tests fail; **M2** — the half-fix, keep only
  the `IsFailure` discard → 8 of 16 fail, all of them exception-path tests; **M3** — move the try/catch
  *around* `ExecuteAsync` instead of inside it → 2 of 17 fail
  (`Handle_WhenOperationIsReplayed_DiscardsBetweenAttempts` and
  `Handle_WhenHandlerThrows_DiscardsBeforeTheTransactionRollsBack`). M3 is the mutant that guards the
  placement, and it matters more than its failure count suggests: under the "around" placement a
  retried command that ultimately *succeeds* never enters the outer catch, so no discard happens on
  any attempt and attempt 2 runs against attempt 1's staged entities — silently. M1 and M2 were
  measured against the 16-test suite, M3 against the 17 tests that include the replay test added for
  this purpose. If a future refactor makes any of the three pass, the suite no longer defends this
  decision.
- **The effect is proven end-to-end, not only the call.** The three mutants above are measured against
  unit tests that assert on an NSubstitute `IUnitOfWork` — they prove the *call*, not that a failed
  command's row stays out of the database. `TransactionBehaviorPersistenceTests`
  (`MicroKit.MediatR.IntegrationTests`) closes that gap: a real EF Core `DbContext` on SQLite, the real
  `AddMicroKitPersistence`/`AddUnitOfWork<TContext>()`/`AddTransactionBehavior()` chain, and **one DI
  scope shared by two commands** — the topology that makes the defect reachable at all. A failed
  command stages a row; a second command in the same scope succeeds and flushes; the assertion, made
  from a fresh `DbContext`, is that only the second command's row exists. Two integration mutants are
  recorded alongside M1–M3:
  **M1-INT** — remove both call sites → both defect tests fail, each reporting the failed command's
  row present alongside the successful one (`["from-successful-command", "from-rejected-command"]` and
  `["from-successful-command", "from-thrown-command"]`); the positive-control test still passes.
  **M2-INT** — the half-fix, keep only the `IsFailure` discard → *only* the exception test fails
  (`["from-thrown-command", "from-successful-command"]`), the business-failure test passes. M2-INT is
  what makes the exception test independently load-bearing rather than a near-duplicate of the
  business-failure one, and it is the mutant that would catch a future "simplification" back to
  Alternative **B**. The suite deliberately carries a positive control
  (`Handle_WhenASingleCommandSucceeds_ItsRowIsPersisted`): without it, both absence assertions would
  pass against a harness that silently writes nothing at all.
  The proof is composed **without** `MicroKit.Messaging` — the scenario raises no events, so the core
  scoped `DomainEventDispatcher` runs unmodified on the `IDomainEventsProvider` that
  `AddUnitOfWork<TContext>()` already supplies. Pulling in the outbox would add a table the scenario
  does not need and let an unrelated module's defects contaminate the failure signal.

---

## ADR-MEDIATR-013: `IDomainEventsDispatcher` Registration Precedence Is a Cross-Module Contract — Core `TryAdd`s, the Glue `Replace`s

**Status:** Superseded by ADR-MEDIATR-014
**Date:** 2026-08-21
**Related:** ADR-MEDIATR-010 (dispatch topology — establishes which implementation is authoritative),
ADR-MEDIATR-009 (`IDomainEventsDispatcher` naming and the `[Obsolete]` `IDomainEventDispatcher` alias),
ADR-MSG-009 (MediatR carve-out for the glue package), ADR-MSG-002 (outbox processing decomposition)

### Decision

Two implementations of `IDomainEventsDispatcher` exist by design, in two different packages. Which
one a container resolves MUST NOT depend on the order in which the two registration methods were
called. That is guaranteed by a **two-sided contract, and only by both sides together**:

| Package | Registration method | Required API | Meaning |
|---------|--------------------|--------------|---------|
| `MicroKit.MediatR` | `AddMicroKitMediatR()` | **`TryAdd`** | Supplies a *default*. Abstains if the slot is already taken. |
| `MicroKit.Messaging.MediatR` | `AddMediatRTransport()` | **`Replace`** | Supplies the *authoritative* implementation. Takes the slot unconditionally. |

With `TryAdd` on one side and `Replace` on the other, both call orders converge on the glue
implementation:

```
AddMicroKitMediatR(); AddMediatRTransport();   → core registers, glue replaces        ✓
AddMediatRTransport(); AddMicroKitMediatR();   → glue registers, core sees it taken    ✓
                                                 and abstains
```

**The rule applies to every dispatcher descriptor these methods register — including the
`[Obsolete]` `IDomainEventDispatcher` alias and the concrete backing registration. There are no
exceptions to remember.**

### Context — the two implementations

> Stated here in full, so this record is readable without opening another one.

- **`MicroKit.MediatR.Events.DomainEventDispatcher`** (core, `internal sealed`) runs **P1 + P2**:
  drain the domain events accumulated on tracked aggregates, then dispatch each one synchronously to
  its registered `IDomainEventHandler<TEvent>`. It **writes nothing to any outbox** — it has no
  knowledge of one. It is the correct and complete dispatcher for a consumer using MicroKit.MediatR
  *without* MicroKit.Messaging, and exists so that package can stand alone.
- **`MicroKit.Messaging.MediatR.Events.DomainEventsDispatcher`** (the glue, `internal sealed`) runs
  the full **P1 → P2 → P3 → P4** sequence: drain, synchronous handler dispatch, notification
  creation via `IDomainEventNotificationFactory`, then a single batched
  `IOutboxWriter.AddBatchAsync` staging every mapped notification in the same transaction.
  **ADR-MEDIATR-010 makes this the authoritative implementation whenever it is installed.**

The two are not interchangeable: the core one is a strict prefix of the glue one. Resolving the core
implementation in an application that installed the glue means P3 and P4 never run — every
integration notification is silently dropped.

Before this decision both sides registered with plain `AddScoped`. Microsoft DI resolves the
**last** registration for a service type, so the winner was decided by call order alone. The
documented order in `AddMediatRTransport`'s `<remarks>` was prescriptive and nothing enforced it —
while, in that same method, the other two overrides were already protected properly
(`Remove` + `Add` guarded by an `InvalidOperationException` for `IOutboxDispatcher`, `Replace` for
`INotificationPublisher`). `IDomainEventsDispatcher` was the only one left positional.

### What breaks if either half is written the other way

Both failure modes are silent — no exception, no log, no startup validation error. Neither is
detectable without reading the DI registration code or observing an empty outbox in production.

- **`Add` instead of `TryAdd` on the core (MicroKit.MediatR) side** — reintroduces the order
  dependency outright. `AddMicroKitMediatR()` called *after* `AddMediatRTransport()` overwrites the
  glue's registration: domain events still reach their `IDomainEventHandler<TEvent>` (P2 runs
  normally, so handler-side effects still happen and nothing looks broken), but no notification is
  ever created or staged. The outbox stays empty. Every downstream consumer simply never receives
  anything.
- **`Add` instead of `Replace` on the glue (MicroKit.Messaging.MediatR) side** — leaves the
  glue-then-core order silently outbox-free, with exactly the same symptom, because the later core
  `Add` wins by position. A `Replace` here is also what makes the glue's registration idempotent and
  consistent with how it already handles `INotificationPublisher`.

**Neither half is sufficient alone.** `TryAdd` on the core side without `Replace` on the glue side
still works only because the glue's `Add` happens to win by position in the core-then-glue order —
an accident of ordering, not a guarantee. `Replace` on the glue side without `TryAdd` on the core
side leaves the glue-then-core order broken. The contract needs both, and each side must be able to
rely on the other honouring it.

### Rationale

1. **The core package must supply a default, not impose one.** MicroKit.MediatR stands alone
   (root principle: each module is autonomous, integration is a bonus). It must therefore register
   *something* for `IDomainEventsDispatcher`, or a consumer without Messaging has no dispatcher at
   all. But "must supply one" does not imply "must win" — `TryAdd` expresses exactly that
   distinction, and it is the standard .NET idiom for it.
2. **Precedence belongs to the higher-level module.** MicroKit.Messaging.MediatR sits above
   MicroKit.MediatR in the dependency graph and knows strictly more (it has an outbox). The lower
   package cannot detect the higher one — the reference direction forbids it — so the only
   mechanism available is for the lower package to yield the slot and the higher one to claim it.
3. **Order-independence is a correctness property, not ergonomics.** Composition-root call order is
   not something a library can police, and here getting it wrong produces no diagnostic at all.
   A rule enforced by the registration API cannot be got wrong by a consumer.
4. **A rule with exceptions is a rule nobody applies.** Guarding two of three dispatcher descriptors
   and leaving the third as `Add` would force every future reader to work out whether that third
   line is deliberate or an oversight. Today the `[Obsolete]` alias happens to be order-insensitive
   in effect — both compatibility adapters resolve `IDomainEventsDispatcher` lazily, so whichever
   descriptor wins delegates to the same winner — but that neutrality is a coincidence of the
   current implementations, not a guarantee. If either adapter is ever changed to capture eagerly,
   the positional race returns silently on a type nobody reads.

### Consequences

- **A consumer who registers their own `IDomainEventsDispatcher` *before* `AddMicroKitMediatR()`
  now keeps it.** Previously the core registration overrode it. This is the intended contract and is
  precisely the mechanism that makes the glue-then-core order safe. A consumer who wants to override
  the core default while calling `AddMicroKitMediatR()` first must now use `Replace`, not `Add` —
  the same API the glue uses.
- **The concrete `DomainEventDispatcher` registration stays unconditional.** It is registered with
  `TryAdd` for idempotency only, never skipped because the interface slot is taken. `TryAdd` there
  cannot express supersession in any case: the type is `internal` and the module ships no
  `InternalsVisibleTo`, so no other assembly can ever register that service type. In the
  glue-then-core order the descriptor is simply inert — nothing resolves it.
- **Calling `AddMicroKitMediatR()` twice no longer appends duplicate dispatcher descriptors.** All
  three resolve exactly once. (This does *not* make the whole method idempotent: a second call that
  scans assemblies still re-registers the `HandlerDispatchMap` and `IDomainEventNotificationFactory`
  singletons, last-wins, discarding the first call's map. That is a separate defect, tracked
  independently and out of scope here.)
- **Enforced by tests, per descriptor.** `DomainEventsDispatcherRegistrationTests`
  (`MicroKit.MediatR.IntegrationTests`) exercises the contract through a real `ServiceProvider` —
  never by inspecting `ServiceDescriptor`s. Because MicroKit.MediatR cannot reference
  MicroKit.Messaging, a prior registration of a stub dispatcher stands in for
  `AddMediatRTransport()`; it is a faithful simulation of the glue-then-core order, since `TryAdd`
  matches on service type alone. Four mutants are recorded as the sensitivity standard, measured
  against the full module suite (163 tests):

  | Mutant | Change | Result |
  |--------|--------|--------|
  | **M1** | `IDomainEventsDispatcher` → `Add` | 2 fail — `…KeepsExistingRegistration` (reproduces the original defect) and `…RegistersDispatcherOnce` |
  | **M2** | all three → `Add` (the pre-decision state) | 2 fail — the same two. At test granularity M2 is indistinguishable from M1: `…RegistersDispatcherOnce` asserts all three descriptor counts and Shouldly stops at the first. The rest of the suite stays green, which is the evidence that this decision changes nothing but precedence and duplication |
  | **M3** | `[Obsolete]` alias → `Add` | 1 fail — `…RegistersDispatcherOnce`, on the alias assertion |
  | **M4** | concrete `DomainEventDispatcher` → `Add`, interface left as `TryAdd` | 1 fail — `…RegistersDispatcherOnce`, on `GetServices(CoreDispatcherType).Count()`: *should be 1 but was 2* |

  **M4 is the load-bearing one.** It is the only mutant that isolates the concrete-type conversion:
  M1 and M3 leave it intact, and M2 masks it behind an earlier assertion. It exists because the
  concrete descriptor is guarded for idempotency rather than supersession, so no behavioural test
  can reach it — without the explicit descriptor-count assertion, M4 is green and the concrete
  `TryAdd` would be untested while appearing covered.

  Three tests are **insensitive by design** and stay green under all four mutants:
  `…ResolvesCoreDispatcher` and both `…ConcreteDispatcherStillResolves` variants. `Add` and `TryAdd`
  are identical when nothing else is registered, so these are regression guards — they prove the
  default is still supplied and the concrete descriptor is never skipped. If a future edit makes any
  of M1–M4 pass, the suite no longer defends this decision.
- **Shipped in two branches.** The core half (`TryAdd`) ships with this record. The glue half
  (`Replace`) ships in MicroKit.Messaging and cites this ADR. Until it lands, core-then-glue
  continues to work by position and glue-then-core is already fixed by the core half alone — so the
  core half is a strict improvement in isolation and opens no window in which the pair is worse than
  before.

---

## ADR-MEDIATR-014: Domain-Event Dispatch Composes by Contribution — One Orchestrator, N Sinks

**Status:** Accepted
**Date:** 2026-08-21
**Supersedes:** ADR-MEDIATR-013 (registration precedence — the race it arbitrates ceases to exist)
**Related:** ADR-MEDIATR-010 (dispatch topology — P1→P4 phase names), ADR-MEDIATR-009
(`IDomainEventsDispatcher` naming and the `[Obsolete]` `IDomainEventDispatcher` alias),
ADR-MEDIATR-012 (`TransactionBehavior` is the caller), ADR-MSG-009 (MediatR carve-out for the glue),
ADR-MSG-011 (`IOutboxWriter.AddBatchAsync`), ADR-MSG-013 (cascade notification publisher), PR #84

### Decision

There is **one** `IDomainEventsDispatcher` implementation in the ecosystem: the core
`DomainEventDispatcher`. It owns the whole sequence — drain, the `IDomainEventHandler<TEvent>` pass,
and the barrier between them and everything downstream. What it does not own, it delegates to an
**ordered, possibly empty collection of `IDomainEventsSink`** resolved from DI.

`MicroKit.MediatR` registers the orchestrator and **zero** sinks. `MicroKit.Messaging.MediatR`
registers **one** sink — the outbox sink, which is today's P3+P4 verbatim. It no longer registers an
`IDomainEventsDispatcher` at all.

```
before                                   after
──────                                   ─────
IDomainEventsDispatcher                  IDomainEventsDispatcher
  ├─ core:  P1 P2                          └─ core: P1 P2 ──► IEnumerable<IDomainEventsSink>
  └─ glue:  P1 P2 P3 P4                                          └─ glue sink: P3 P4
     ▲ one slot, two claimants,                              ▲ one slot, one claimant,
       last Add* wins                                          N contributors, order-free
```

Because Microsoft DI resolves `IEnumerable<T>` to *every* registration for `T`, the composition is
order-independent by construction, not by contract. Nothing arbitrates; nothing can lose.

### Context — why the previous shape needed arbitrating at all

> Stated in full so this record is readable without opening another one.

`DomainEventDispatcher` (core, `internal sealed`) runs drain + synchronous handler dispatch and
writes to no outbox — the correct and complete dispatcher for MicroKit.MediatR standing alone.
`DomainEventsDispatcher` (glue, `internal sealed`) runs drain + handler dispatch + notification
creation + a single batched `IOutboxWriter.AddBatchAsync`. Both registered against the same service
type; Microsoft DI resolves the last registration; the winner was decided by composition-root call
order. ADR-MEDIATR-013 fixed that with `TryAdd` on the core side and `Replace` on the glue side, and
PR #84 shipped the core half.

That decision is correct about precedence and is not being reversed on its merits. What this record
challenges is the premise underneath it: **that the two implementations are alternatives at all.**

Read side by side, the core is a *strict prefix* of the glue — identical collaborators, identical
calls, identical order, differing only in a `Count == 0` early return that the glue added and the
core did not. The glue does not replace the core; it re-implements it and appends. Alternatives get
arbitrated. Extensions get composed. Everything ADR-MEDIATR-013 had to specify — a two-sided
contract, an exception-free rule covering three descriptors, four recorded mutants, two coordinated
branches — is the cost of arbitrating between two things that were never in competition.

The choice of transport is not a race to be won. It is a contribution to be collected.

### Rationale

**1. The glue extends the core; the code says so.** The evidence is textual, not interpretive:
the two `foreach (var domainEvent in domainEvents) await handlerDispatcher.DispatchAsync(...)` loops
are the same loop. When one implementation is a prefix of another, the honest factoring is
orchestrator-plus-tail, not two rivals for one slot.

**2. Duplication that has already drifted, will drift again — invisibly.** The `Count == 0` guard
exists in the glue and not the core. Harmless today. But any future change to drain or P2 semantics
must now be made twice, in two repositories' worth of review, and **no test compares the two**. The
divergence is undetectable by construction: each is tested against its own expectations. One
orchestrator has one behaviour and one place to change it.

**3. The current shape admits exactly two participants — forever.** This is the strongest argument
and the one that outlives the immediate bug. A third in-transaction participant — an audit sink, an
in-transaction read-model projector, a `MicroKit.Observability` sink, an in-memory test sink — cannot
join without either another `Replace` war or a decorator chain in which every participant must know
how to reconstruct the one before it. That reconstruction is not hypothetical: `AddMediatRTransport`
already performs it for `IOutboxDispatcher`, and the shape it takes is a `LastOrDefault` descriptor
hunt plus a three-branch `CreateInner` reflecting over `ImplementationInstance` /
`ImplementationFactory` / `ImplementationType`
(`MessagingMediatRExtensions.cs:56-71, 85-94`). That is what N=2 already costs. Sinks are O(N) with
no acrobatics at any N.

**4. Precedence between modules stops being a thing modules must agree on.** ADR-MEDIATR-013 §
"Neither half is sufficient alone" is precisely the problem: two packages, released independently,
each depending on the other having honoured a rule neither can verify. `TryAddEnumerable` needs no
counterpart. The low package cannot detect the high one — that constraint is unchanged — but it no
longer needs to.

**5. The orchestrator keeps what is genuinely orchestration.** The documented two-foreach invariant —
P2 completes for *every* event before any notification work begins, so a P2 handler can never observe
a partially written outbox batch (`DomainEventsDispatcher.cs:31-35`) — is a sequencing guarantee
across participants. It belongs to whoever sequences them, and survives the refactor unchanged
because the orchestrator, not the sink, still owns the barrier. Had the drain been recursive, this
would have been the argument *against* the seam; it is single-pass in both implementations, so it is
the argument for placing the seam exactly where it is placed.

**6. P3 was never a shared phase, which is why the extracted piece is a sink and not a phase.**
Notification mapping through `IDomainEventNotificationFactory` is the outbox path's own business.
The orchestrator has no opinion about notifications and gains none. The sink receives raw
`IDomainEvent`s and maps them itself — no new contract crosses the seam beyond the sink interface.

**7. PR #84's `TryAdd` stays correct and must not be unwound.** The core still supplies a *default*
`IDomainEventsDispatcher`, and a consumer who registers their own before `AddMicroKitMediatR()` must
still keep it. That is exactly what `TryAdd` expresses and it is orthogonal to how many
implementations ship. What changes is only that MicroKit no longer ships a second claimant — so the
`Replace` half specified by ADR-MEDIATR-013 for the glue never needs to ship. **`TryAdd` is not
vestigial; do not revert it.**

### What the current design fails at, and what this removes

| # | Failure mode (current) | Removed? |
|---|---|---|
| 1 | Wrong implementation resolved by call order — silent, no exception, symptom is an empty outbox in production | ✅ one implementation, nothing to lose a race |
| 2 | Drain + P2 duplicated across two modules; already drifted (`Count == 0`); no test compares them | ✅ one orchestrator |
| 3 | Closed to a third in-transaction participant without a `Replace` war or a decorator chain | ✅ `TryAddEnumerable`, O(N) |
| 4 | `AddMediatRTransport` must re-implement a sequence it does not own to append to it | ✅ registers its own contribution only |
| 5 | The `[Obsolete]` `IDomainEventDispatcher` alias must be registered by both packages, so any rule must be applied twice (ADR-MEDIATR-013 § "no exceptions to remember" exists for this) | ✅ registered once, by core |
| 6 | Correctness depends on two independently released packages each honouring an unverifiable contract | ✅ no counterpart required |

**Not removed, and stated so nobody assumes otherwise:**

- Sinks run **in-transaction, ordered, fail-fast** — a throwing sink aborts the command, exactly as a
  throwing P4 does today. With N sinks the blast radius is wider, so the contract must say plainly:
  a sink stages work in the caller's unit of work; it is not a place for I/O to an external system.
- A sink registered twice writes twice. `TryAddEnumerable` (which deduplicates on
  `(ServiceType, ImplementationType)`) is the guard, and `AddMediatRTransport` must use it. This is
  the direct analogue of PR #84's `TryAdd` and inherits its reasoning.
- **Cascade dispatch (ADR-MSG-013) must keep working, and does.** `DomainEventsCascadeNotificationPublisher`
  resolves `IDomainEventsDispatcher` and calls it after all notification handlers, so cascade events
  raised post-commit are staged to the outbox in the same processor scope. Under this design it
  resolves the single orchestrator, which runs the sinks — cascade events still reach the outbox.
  This is the one place where a careless implementation would break ADR-MSG-013 silently; the
  integration test named below exists to pin it.
- Neither dispatcher re-drains after P2, so a P2 handler that dirties another aggregate does not get
  its events dispatched in the same pass. **Pre-existing, unchanged, explicitly out of scope.**

### Alternatives Rejected

| # | Alternative | Why it loses |
|---|---|---|
| **A** | **Finish the `Replace` repair** (ship the glue half of ADR-MEDIATR-013) | The cheapest option by a wide margin — one line plus a test — and it does fix failure mode 1. It fixes nothing else. Duplication (2) remains and keeps drifting; the two-participant ceiling (3) remains; `AddMediatRTransport` still re-implements a sequence it does not own (4); the alias still needs registering twice (5); correctness still rests on two packages honouring an unverifiable mutual contract (6). It buys order-independence for one service type at the price of permanently ratifying the shape that made order matter. **This is the honest baseline and it is defensible for a team that wants to stop here** — see Migration cost. |
| **B** | Decorator chain: the glue decorates the core `IDomainEventsDispatcher` | Removes the duplication but not the ordering problem — who is outermost is still positional — and every decorator must reconstruct the descriptor beneath it. The cost is already visible in `AddMediatRTransport`'s `LastOrDefault` + three-branch `CreateInner` for `IOutboxDispatcher`. At N=3 it is worse than what it replaces. |
| **C** | Builder opt-in `cfg.UseOutboxDispatch()` as the **selection** mechanism | Mechanically sound — `MediatRBuilder.Services` is public and `BehaviorExtensions` proves the pattern — and it genuinely converts a silent misconfiguration into a compile error when the glue package is absent. It still loses. Selection is only needed while two claimants exist; with sinks there is nothing to select. Worse, layered on top of the sink model it splits one feature's wiring across two builders — `AddMediatRTransport()` for the `IOutboxDispatcher` decorator and `INotificationPublisher`, `UseOutboxDispatch()` for the sink — making a *new* two-call requirement and relocating the silent failure from "wrong order" to "forgot the second call". One feature, one entry point. |
| **D** | A full three-seam phase model (a drainer seam + a publisher seam + sinks) | Two of the three already exist: `IDomainEventsProvider` (`MicroKit.Domain`, Level 0) and `IDomainEventHandlerDispatcher` (scoped, core). Both are DI-swappable today. Parallel seams over them would duplicate live interfaces and give a reader two ways to replace one thing. |
| **E** | Startup validation: throw when the glue is installed but the core dispatcher resolved | MicroKit.MediatR (Level 2) cannot detect MicroKit.Messaging (Level 3) — the reference direction forbids it. It would need a marker service in the low package naming the high one, i.e. the inversion the graph exists to prevent. |
| **F** | Give `IDomainEventsSink` an explicit `Order` property | YAGNI at N=1. Registration order is deterministic and sufficient. **Deletion criterion, so this can be checked rather than argued:** add `Order` when two sinks ship whose relative order is load-bearing — not before. |

### The contract

```csharp
namespace MicroKit.MediatR.Events;   // MicroKit.MediatR.Abstractions

/// <summary>
/// Receives the batch of domain events drained for one dispatch pass, after every
/// <see cref="IDomainEventHandler{TEvent}"/> has completed for every event in the batch.
/// </summary>
public interface IDomainEventsSink
{
    /// <summary>Receives one drained batch. Never called with an empty batch.</summary>
    ValueTask ReceiveAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken ct = default);
}
```

`ValueTask` per the root convention and matching `IOutboxWriter.AddBatchAsync`, which the one real
sink wraps. `IDomainEventsDispatcher.DispatchEventsAsync` returns `Task`; that is ADR-MEDIATR-009's
shipped surface and is not worth churning in preview.

### Consequences

- **New public contract, additive:** `IDomainEventsSink` in `MicroKit.MediatR.Abstractions`. It is
  placed there rather than beside `IDomainEventsDispatcher` in core because it is a contract
  *implemented by other packages*; `IDomainEventsDispatcher` is an orchestration seam consumed
  in-module, and moving it during preview buys nothing. **`api-reviewer` approval is required
  before merge** (module `.claude/CLAUDE.md`, public-API rule).
- **`MicroKit.Messaging.MediatR`'s `DomainEventsDispatcher` becomes `OutboxDomainEventSink`.** It
  sheds `IDomainEventsProvider` and `IDomainEventHandlerDispatcher` (now the orchestrator's) and
  keeps `IDomainEventNotificationFactory`, `OutboxMessageFactory`, `IOutboxWriter`,
  `IExecutionContext`. Both types are `internal sealed`, so **no consumer-visible type changes**.
- **`AddMediatRTransport()` keeps its signature and its call site.** It drops three dispatcher
  registrations (`IDomainEventsDispatcher`, the concrete type, the `[Obsolete]` alias) and adds one
  `TryAddEnumerable(ServiceDescriptor.Scoped<IDomainEventsSink, OutboxDomainEventSink>())`. Its
  `<remarks>` loses the prescriptive call-order paragraph — call order genuinely no longer matters
  for the dispatcher. Its `IOutboxDispatcher` decorator and `INotificationPublisher` replacement are
  untouched by this decision.
- **`AddMicroKitMediatR`'s `TryAdd` for all three dispatcher descriptors is unchanged, and its
  `<remarks>` are rewritten.** The paragraph promising that "the glue registers with `Replace`" is
  now false and must be replaced with the sink model. The `TryAdd` itself stays — it still protects
  a consumer's own dispatcher registered before the call. Reverting it to `Add` remains a defect.
- **ADR-MEDIATR-013 is superseded, not deleted.** Its precedence rule has no object once the glue
  registers no dispatcher: `TryAdd`-versus-`Replace` arbitrates a race that no longer occurs. Its
  core half shipped in PR #84 and remains correct for the different reason stated in Rationale §7.
  Its glue half (`Replace`) is **cancelled and must not ship**.
- **Pipeline impact: none.** No behavior is added, removed, or reordered. `TransactionBehavior`
  (order 700) remains the sole in-pipeline caller of `DispatchEventsAsync`, still inside the
  `ITransactionalContext.ExecuteAsync` operation and before `IUnitOfWork.CommitAsync`
  (ADR-MEDIATR-012), so sinks run in-transaction and outbox rows still commit atomically with the
  domain changes. The second, out-of-pipeline caller — `DomainEventsCascadeNotificationPublisher`,
  post-commit in the outbox processor scope — also reaches the sinks unchanged.
- **Test rework in `MicroKit.MediatR.IntegrationTests`.** `DomainEventsDispatcherRegistrationTests`
  and its four recorded mutants (M1–M4) test precedence between two claimants. Post-decision the
  interface has one implementation, so the stub that "faithfully simulates the glue-then-core order"
  now simulates only a *consumer override* — which is still a real, supported case. **Re-purpose and
  rename these tests; do not delete them.** M1 (`IDomainEventsDispatcher → Add`) and M4 (concrete
  type → `Add`) stay meaningful and stay recorded; M2 and M3 lose their glue framing.
- **New sensitivity standard, to be recorded when the work lands.** The mutant that matters is
  *"delete the sink loop from the orchestrator"* — it must fail an integration test asserting outbox
  rows exist after a command, and a second asserting they exist after a **cascade** publish
  (ADR-MSG-013). A registration test asserting `GetServices<IDomainEventsSink>().Count() == 1` after
  calling `AddMediatRTransport()` **twice** pins the `TryAddEnumerable`. The intended suite:

  ```
  DispatchEventsAsync_WhenNoSinkRegistered_DispatchesHandlersOnly
  DispatchEventsAsync_WhenSinkRegistered_ReceivesBatchAfterAllHandlersRan
  DispatchEventsAsync_WhenBatchIsEmpty_DoesNotInvokeAnySink
  DispatchEventsAsync_WhenSinkThrows_PropagatesAndRollsBack
  AddMediatRTransport_CalledTwice_ContributesTheSinkOnce
  AddMicroKitMediatR_WhenDispatcherAlreadyRegistered_KeepsIt        (PR #84 TryAdd, re-purposed)
  Command_WhenMappedEventRaised_StagesOutboxRowInSameTransaction     (Messaging.MediatR)
  CascadePublish_WhenHandlerRaisesEvent_StagesOutboxRowInProcessorScope  (ADR-MSG-013)
  ```

  Shouldly assertions, NSubstitute doubles; a real EF Core `DbContext` on SQLite for the two staging
  tests — the unit tests prove the *call*, only the integration tests prove the *row*.
- **No dependency-graph change.** No new edge in either direction. `MicroKit.MediatR` (Level 2)
  still knows nothing of `MicroKit.Messaging` (Level 3). `dependency-guardian` review is required
  only because two `.csproj` files may gain nothing at all — verify, do not assume.
- **Migration cost, stated plainly.** Two modules, one new public contract, one type moved and
  re-shaped, three registration sites edited, one XML-doc block rewritten, one ADR superseded, one
  test class re-purposed, CHANGELOG entries in both modules, and a **coordinated release** of
  MicroKit.MediatR and MicroKit.Messaging (the glue will not compile against a core without
  `IDomainEventsSink`). Days, not hours. Alternative **A** is one line and an hour. The case for
  paying the difference is failure modes 2–6, all of which A leaves standing, and the fact that both
  packages are still `1.0.0-preview.*` — the contract is additive and no consumer-visible type
  changes, so this is the cheapest it will ever be. After 1.0.0 stable, `IDomainEventsSink` becomes a
  permanent surface and this becomes a v2 conversation.

---

## ADR-MEDIATR-015: Domain-Event Composition Fails Loudly — Unreachable Notifications, `AddMediatRDomainEvents`, and Order-Independent Decoration

**Status:** Accepted
**Date:** 2026-08-21
**Related:** ADR-MEDIATR-014 (one orchestrator, N sinks — implemented in the same lot as this record),
ADR-MEDIATR-013 (superseded by 014), ADR-MEDIATR-012 (`TransactionBehavior` is the caller),
ADR-MEDIATR-005 (one notification type per event type), ADR-MSG-009 (MediatR carve-out for the glue),
ADR-MSG-013 (cascade notification publisher), ADR-EXEC-001 (consume a contract, do not depend on its
implementer), PR #84

### Decision

Three changes, one theme: **every remaining way this composition could be wrong now produces a
throw, not silence.**

1. **A notification with no sink is a configuration error.** When the core orchestrator drains an
   event that maps to a `DomainEventNotification<TEvent>` and **no `IDomainEventsSink` is
   registered**, it throws `InvalidOperationException` naming the event type, the notification type,
   and the missing registration. Zero sinks with no notifications stays valid and free.
2. **`AddMediatRTransport()` is renamed `AddMediatRDomainEvents()`**, outright, with no
   `[Obsolete]` alias.
3. **`AddInProcessTransport()` registers with `TryAdd`, and `AddMediatRDomainEvents()` is
   idempotent.** The `IOutboxDispatcher` decoration can no longer be displaced by a later transport
   registration, nor double-applied by a second glue call.

### Context

ADR-014 removed the wrong-implementation race by removing the second implementation. It did not
address three other ways the same composition fails, all silent, none of which the sink model
touches on its own.

**Unreachable notifications.** `DomainEventNotification<TEvent>` subclasses are discovered by the
assembly scan and mapped in `AddMicroKitMediatR`. Nothing consumes that map unless a sink is
registered. A consumer who declares notifications, wires everything else correctly, and simply never
installs a sink gets an application that starts, runs, dispatches handlers, and publishes nothing —
no exception, no log, no startup error.

**The transport misnomer.** `AddMediatRTransport()` transports nothing, and
`microkit-messaging-naming.md` reserves the `Add{Provider}Transport()` shape for broker providers
(`AddRabbitMqTransport`). After ADR-014 the method contributes a sink, decorates `IOutboxDispatcher`,
and replaces `INotificationPublisher` — three things, none of them a transport.

**Decorator displacement.** `AddInProcessTransport()` registered `IOutboxDispatcher`,
`IMessagePublisher` and `IMessageSerializer` with a plain `Add`. Called *after* the glue it appended
a second `IOutboxDispatcher` descriptor; Microsoft DI resolves the last one, so
`MediatROutboxDispatcher` was bypassed and every domain-event notification stopped being published —
while the outbox kept draining and reporting success. The serializer stacked a duplicate for the
same reason, and the glue's `LastOrDefault` descriptor hunt meant a second glue call found its *own*
factory descriptor and wrapped it twice.

### Rationale

**1. ADR-014's stated ground for rejecting Alternative E does not hold, and the correct ground is
different.** ADR-014 rejected startup validation because "MicroKit.MediatR (Level 2) cannot detect
MicroKit.Messaging (Level 3) — it would need a marker service in the low package naming the high
one, i.e. the inversion the graph exists to prevent." That reasoning does not survive ADR-014 itself.
`IDomainEventsSink` lives in `MicroKit.MediatR.Abstractions`, names no package, and any package may
register one. A low package asking "is *any* sink registered?" names nothing high and inverts
nothing — it is the same shape as ADR-EXEC-001, where Messaging consumes `IExecutionScopeFactory`
without depending on Tenancy.

The correct ground is narrower, and it is why this is a new check rather than a wholesale
reinstatement of Alternative E: **the sink model makes the wrong-implementation race *impossible*,
but it does not make the zero-sink-with-notifications case *detectable*.** Alternative E asked
"which dispatcher won?", a question that no longer exists. This asks "is anything going to receive
what the scan mapped?", a question ADR-014 neither answers nor removes. An ADR superseded on a
correct-but-outdated basis and one superseded because its reasoning was wrong are different things;
this is the second kind, and recording which is which is the point of this section.

**2. The check is per drained event that actually maps, not per registration.** "Notifications exist
and no sink is registered" is the condition as first stated, and it over-fires: an application may
legitimately declare notifications it publishes by another route, and an event with no mapping loses
nothing. Throwing only when a *mapped event is about to be dropped* never produces a false positive,
and lets the message name the concrete event and notification rather than a count.

**3. The legitimate zero-sink path stays free, which is what forced one bit of metadata.** The
orchestrator tests `_sinks.Length != 0` first, so the check never runs when a sink exists. With no
sink, a precomputed `_mustGuardNotifications` (`_sinks.Length == 0 && !catalog.IsEmpty`, evaluated
once per scope) returns immediately for a consumer with no notification mappings. Without
registration-time metadata the only way to ask "does this event map?" would be
`IDomainEventNotificationFactory.Create`, which **constructs** a notification — a call and an
allocation per event, on every batch, on the path that must be free. `DomainEventNotificationCatalog`
wraps the `notificationTypeMap` the scan already built and previously discarded; it adds no
computation and no public surface.

**4. First dispatch is the honest timing, and no package reference is worth changing it.**
MicroKit.MediatR has no post-composition-root hook: no `IValidateOptions`, no `IStartupFilter`, no
`IHostedService`, and core references only `MediatR` and
`Microsoft.Extensions.DependencyInjection.Abstractions`. `Microsoft.Extensions.Options` is available
in CPM but adding it puts a runtime dependency on a Level 2 package for a diagnostic, and
`IValidateOptions` fires only when the options object is resolved — so it would not reliably run at
startup either. **This therefore fires on the first dispatch of a mapped event, not at startup, and
an application that never raises such an event never sees it.** That is the same timing as a missing
DI registration, and it is stated here rather than left to be discovered.

**5. Throw, do not log.** This is data loss, not a warning. `TransactionBehavior` (order 700) calls
`DispatchEventsAsync` inside `ITransactionalContext.ExecuteAsync` and before `IUnitOfWork.CommitAsync`
(ADR-MEDIATR-012), so the throw rolls the command back. A failed command is strictly better than one
that succeeds while discarding an integration notification, and a log line on a path nobody is
watching is how the original defect stayed invisible.

**6. The rename is free now and never again.** Both packages are `1.0.0-preview.*`, there are zero
external consumers, and the only invocation in the repository is one integration-test harness. An
`[Obsolete]` alias would preserve a name whose entire problem is that it misdescribes the method, and
would have to be carried to 1.0.0 and beyond. The new name follows the module's own
`Add{Technology}{Concern}` shape (`AddEfCoreOutbox`), and every registration it makes sits on the
domain-event → notification path.

**7. `TryAdd` on a transport is the same idea as `TryAdd` on the dispatcher, applied one level
down.** A transport supplies a default and abstains if something already holds the slot — the idiom
`AddMicroKitMessaging` already uses for `TimeProvider` and `Random`, with the same justification. It
converts the displacement failure from silent to impossible, in three tokens, with no new mechanism.
The one order that still fails — the glue before any transport — already threw, and still throws,
because there is genuinely nothing to decorate.

### Consequences

- **New internal type `DomainEventNotificationCatalog`** (`MicroKit.MediatR`, `internal sealed`),
  registered as a singleton by `AddMicroKitMediatR`. **`IDomainEventNotificationFactory`'s public
  contract is unchanged**, and no public type is added by this record.
- **`DomainEventDispatcher` gains two constructor parameters** (`IEnumerable<IDomainEventsSink>` from
  ADR-014, and the catalog). It is `internal` with no `InternalsVisibleTo`, so this is not
  consumer-visible.
- **`AddMediatRTransport()` no longer exists.** Consumers call `AddMediatRDomainEvents()`. Its
  `<summary>` states the three things it registers; its `<remarks>` lose the prescriptive
  dispatcher call-order paragraph, which ADR-014 made false.
- **`microkit-messaging-naming.md` gains its own row for `AddMediatRDomainEvents()`, deliberately
  NOT under the `Add{Provider}Transport()` pattern** — this method is not a transport, and the
  `Add{Provider}Transport()` row is narrowed to brokers so the reservation is explicit.
- **`AddInProcessTransport()` is now idempotent and non-displacing.** A consumer who relied on
  calling it twice to *replace* an earlier registration no longer can; nothing in the repository did,
  and "last call wins" was never a documented contract of that method.
- **The reflective `CreateInner` stays**, with its `IL2072`/`IL3050` suppressions. The inner type
  (`InProcessIntegrationDispatcher`) is `internal` to Messaging Core and cannot be named from the
  glue, so removing the reflection is a separate design question, not a side effect of this one. It
  is recorded here so it is not mistaken for an oversight.
- **Sensitivity standard.** The mutant that matters for ADR-014 — *delete the sink loop from the
  orchestrator* — kills **7 tests**: 3 in `MicroKit.MediatR.IntegrationTests`
  (`DomainEventDispatchCompositionTests`) and 4 in `MicroKit.Messaging.MediatR.IntegrationTests`
  (both staging tests plus `NominalPathTests` and `InboxDuplicateObservationTests`, which depend on
  outbox rows existing). For this record specifically:

  ```
  DispatchEventsAsync_WhenMappedEventRaisedAndNoSink_ThrowsNamingTheRegistration
  DispatchEventsAsync_WhenUnmappedEventRaisedAndNoSink_DoesNotThrow    (per-event precision)
  DispatchEventsAsync_WhenMappedEventRaisedAndSinkRegistered_DoesNotThrow
  AddMediatRDomainEvents_CalledTwice_ContributesTheSinkOnce             (TryAddEnumerable)
  AddMediatRDomainEvents_RegistersNoDomainEventsDispatcher              (ADR-014 invariant)
  AddInProcessTransport_CalledAfterTheGlue_KeepsTheDecorator            (the silent displacement)
  AddMediatRDomainEvents_WhenNoTransportRegistered_ThrowsNamingTheFix   (the loud order)
  ```

- **`TryAddEnumerable` requires an implementation type or an instance.** A factory-based descriptor
  has no implementation type and throws *"indistinguishable from other services"* rather than
  registering. This is documented on `IDomainEventsSink` because a sink author will otherwise meet it
  as a runtime surprise.
- **Not fixed, and deliberately so:** cascade rows staged on the outbox-processing path are still
  never flushed — `TransactionBehavior` is the sole `SaveChanges` owner (ADR-MSG-012) and is not on
  that path (`L0-FINDINGS.md` Finding #3). `CascadePublish_WhenHandlerRaisesEvent_StagesOutboxRowInProcessorScope`
  asserts **staging**, not persistence, exactly as its name says; `CascadeObservationTests` continues
  to pin the resulting loss. This record fixes the half ADR-014 owns — that cascade dispatch reaches
  the sinks at all — and leaves Finding #3 open.
- **Coordinated release still required** (ADR-014): the glue will not compile against a core without
  `IDomainEventsSink`.

### Alternatives Rejected

| # | Alternative | Why it loses |
|---|---|---|
| **A** | **Registration-level check** — throw on the first non-empty dispatch whenever any notification type was discovered and no sink is registered | The condition as first stated, and the simpler code. It over-fires: an application may legitimately declare notifications it publishes by another route, and an event that maps to nothing loses nothing by having no sink. It also cannot name the offending event or notification, so the message degrades to a count. Concretely, it would throw inside ADR-014's own `DispatchEventsAsync_WhenNoSinkRegistered_DispatchesHandlersOnly`, because the MicroKit.MediatR integration-test assembly declares notification subclasses — forcing that test into a contrived scan scope to say something it should say plainly. |
| **B** | **Container-build validation** via a new `Microsoft.Extensions.Options` reference and `IValidateOptions` | Moves the failure from first dispatch to `BuildServiceProvider`, which is genuinely better timing. It costs a runtime package reference on a Level 2 package for a diagnostic, and it does not actually deliver: `IValidateOptions` runs when the options object is first resolved, not at build, unless the host opts into eager validation. Paying a permanent dependency for a conditional improvement is the wrong trade in preview. |
| **C** | **A single registered `bool`** instead of `DomainEventNotificationCatalog` | Smaller metadata, and it was the first instinct. But naming the notification type in the message then requires calling `IDomainEventNotificationFactory` from the orchestrator — dragging notification mapping into the type ADR-014 §6 deliberately keeps ignorant of it, and constructing a notification purely to describe an error. The catalog costs nothing extra (the map already exists and was being discarded), allocates nothing on lookup, and yields the type directly. |
| **D** | **Builder opt-out** — `cfg.AllowNotificationsWithoutSink()` | Public surface for a configuration that has no known use. The per-event check already permits every case this would need to permit: an event with no mapping never throws. Add it if a real consumer appears with notifications they publish by another route — not before. |
| **E** | **`[Obsolete]` shim for `AddMediatRTransport()`** | Preserves the exact name whose problem is that it misdescribes the method, and commits to carrying it past 1.0.0. Zero external consumers, one repository call site, both packages in preview: this is the cheapest the rename will ever be. |
| **F** | **Loud guard only for the decorator** — leave `AddInProcessTransport()` on `Add` and have the glue detect displacement | Cannot be done: the glue runs *before* the displacing call, so there is nothing for it to detect. It would need a hook after composition, which is the problem B already fails to solve. `TryAdd` removes the failure instead of reporting it. |
| **G** | **Rewrite the `IOutboxDispatcher` seam** as an ordered chain, the way ADR-014 rewrote dispatch | The principled fix, and out of scope. ADR-014 explicitly leaves this seam untouched, the brief for this lot asks for the smallest change that removes the silent failure, and a decorator chain for `IOutboxDispatcher` needs its own decision about ordering and about naming the internal inner type across the package boundary. `TryAdd` plus idempotency removes the defect today without foreclosing that. |
