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
| `IApplicationEvent : IEvent` | `MicroKit.MediatR.Abstractions` | Application-layer fact — emitted by application code, not aggregates |

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
