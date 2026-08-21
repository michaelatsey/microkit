# Changelog — MicroKit.MediatR

All notable changes to this package are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) — [Semantic Versioning](https://semver.org/).

---

## [Unreleased]

### Breaking Changes

**MicroKit.MediatR.Behaviors**
- `TransactionBehavior<TRequest, TResponse>` gains a third public constructor parameter,
  `IUnitOfWork` (from `MicroKit.Persistence.Abstractions`). The constructor is now
  `(ITransactionalContext, IDomainEventsDispatcher, IUnitOfWork)`. This is binary- and
  source-breaking for code that constructs the behavior manually; it is transparent for consumers
  registering it through `AddTransactionBehavior()`, which resolves it as an open generic from DI.
  **Migration:** pass the third argument, or resolve the behavior from the container.
  `AddTransactionBehavior()` now additionally requires `IUnitOfWork` to be registered — see
  `AddUnitOfWork<TContext>()` in `MicroKit.Persistence.EntityFrameworkCore`.

### Added

**MicroKit.MediatR.Abstractions**
- `IDomainEventSink` — the seam a higher-level package implements to participate in domain-event
  dispatch. `ValueTask ReceiveAsync(IReadOnlyList<IDomainEvent>, CancellationToken)`, called once
  per drained batch, after every `IDomainEventHandler<TEvent>` has completed for every event in it.
  Sinks run in-transaction, in registration order, fail-fast: a sink stages work in the caller's
  unit of work and is not a place for I/O to an external system. Register with `TryAddEnumerable`
  and an implementation type or instance — never a factory lambda, which `TryAddEnumerable` rejects
  because it cannot deduplicate one. Additive; no consumer-visible type changed (ADR-MEDIATR-014).

### Changed

**MicroKit.MediatR**
- **Domain-event dispatch composes by contribution: one dispatcher, N sinks (ADR-MEDIATR-014).**
  There is now a single `IDomainEventsDispatcher` implementation in the ecosystem — the core
  orchestrator. It drains, runs every `IDomainEventHandler<TEvent>` for every event, and then hands
  the batch to an ordered, possibly empty `IEnumerable<IDomainEventSink>` resolved from DI.
  `MicroKit.MediatR` registers the orchestrator and **zero** sinks; `MicroKit.Messaging.MediatR` now
  contributes the outbox sink instead of registering a rival dispatcher.

  Previously two implementations competed for one service type and the winner was decided by
  composition-root call order — silently, with an empty outbox as the only symptom. Because
  Microsoft DI resolves `IEnumerable<T>` to *every* registration for `T`, the composition is now
  order-independent by construction rather than by contract: nothing arbitrates and nothing can
  lose. **This supersedes the two-sided `TryAdd`/`Replace` precedence rule announced here
  previously** (ADR-MEDIATR-013); the `Replace` half was never shipped and is now cancelled.
  **Migration:** none for consumers — both dispatcher types were `internal`. A module that extended
  domain-event dispatch by registering its own `IDomainEventsDispatcher` should now contribute an
  `IDomainEventSink` instead.

- `AddMicroKitMediatR()` registers `IDomainEventsDispatcher`, its concrete backing type and the
  `[Obsolete]` `IDomainEventDispatcher` alias with `TryAdd` rather than `Add`. This shipped for the
  precedence rule above and **remains correct for a different reason**: the core supplies a
  *default*, so a consumer who registers their own dispatcher *before* this call keeps it. Reverting
  any of the three to `Add` is a defect. A side effect: calling `AddMicroKitMediatR()` twice no
  longer appends duplicate dispatcher descriptors.
  **Migration:** a consumer who overrode the dispatcher by registering *after* `AddMicroKitMediatR()`
  is unaffected (a later `Add` still wins). One who registered *before* it now keeps theirs, where
  previously it was overridden. To override regardless of order, use `Replace`.

- **A domain-event notification with no sink now throws instead of vanishing (ADR-MEDIATR-015).**
  When the scanned assemblies declare `DomainEventNotification<TEvent>` subclasses but no
  `IDomainEventSink` is registered, every notification was silently discarded: the application
  started, ran, dispatched handlers, and published nothing — no exception, no log, no startup error.
  The orchestrator now throws `InvalidOperationException` naming the concrete event type, the
  concrete notification type, and the registration that is missing.

  It fires on the **first dispatch of an event that actually maps to a notification**, not at
  startup — MicroKit.MediatR has no post-composition-root hook and gains no package reference for
  one — so an application that never raises such an event never sees it. Declaring no notifications
  and registering no sink stays valid and costs one bool per batch: the handlers-only configuration
  is supported and unaffected.
  **Migration:** register a sink — `AddMediatRDomainEvents()` from `MicroKit.Messaging.MediatR`, or
  your own via `TryAddEnumerable` — or delete the notification types you do not route.

**MicroKit.MediatR.Behaviors**
- No API change. `TransactionBehavior` remains the sole in-pipeline caller of `DispatchEventsAsync`
  (constructor parameter 2), still inside the `ITransactionalContext.ExecuteAsync` operation and
  before `IUnitOfWork.CommitAsync`, so sinks run in-transaction and outbox rows still commit
  atomically with the domain changes. *Which* dispatcher runs is no longer a question — there is
  one. Pipeline order, behavior count and timing are all unchanged.

**MicroKit.MediatR.Testing**
- No API change. `FakeDomainEventDispatcher` implements `IDomainEventsDispatcher`, so registering it
  in a container *before* `AddMicroKitMediatR()` takes effect, where once the real dispatcher
  silently overwrote it. Its primary documented use — direct construction and injection into a
  behavior under test — is unaffected. Note it replaces the whole orchestrator, sinks included; to
  observe sink behaviour, register a fake `IDomainEventSink` and keep the real dispatcher.

### Fixed

**MicroKit.MediatR.Behaviors**
- `TransactionBehavior` committed an empty transaction on every transactional command — a silent
  no-write, not a caught error. It opened a database transaction, let the command handler stage
  aggregates in the change tracker, dispatched domain events (which stage outbox rows in that same
  change tracker), and then committed the database transaction **without anything ever calling
  `SaveChangesAsync`**. `ITransactionalContext.ExecuteAsync` only performs
  `BeginTransactionAsync` → operation → `IDbContextTransaction.CommitAsync`; the flush lives in
  `IUnitOfWork.CommitAsync`, which nothing on that path invoked. The behavior now awaits
  `IUnitOfWork.CommitAsync` **after** `IDomainEventsDispatcher.DispatchEventsAsync`, inside the
  same `ExecuteAsync` operation, so aggregates and outbox rows are written by a single
  `SaveChangesAsync` within the open transaction. The ordering is load-bearing: flushing before the
  dispatch would drop every outbox row the dispatch had just staged. A business failure
  (`Result.IsFailure`) still dispatches nothing and now also flushes nothing.
- `TransactionBehavior` now calls `IUnitOfWork.DiscardChanges()` on **every** non-commit exit —
  business failure *and* thrown exception. Previously neither path cleared the change tracker.
  `DbContext` is scoped, not per-command: the entities a failed command staged stayed in the tracker
  as `Added`/`Modified`, and the next `SaveChangesAsync` in that scope — from any later command —
  wrote them. A command that explicitly failed its business rule persisted its data anyway, with no
  error, no log, and nothing distinguishing it from correct operation. The exception path had the
  identical defect: a database transaction rollback undoes what was *written* but does not reset the
  pending change set, so staged entities survive a rollback exactly as they survive a business
  failure — which is why a fix scoped to `Result.IsFailure` would have been half a fix. The discard
  runs inside the `ITransactionalContext.ExecuteAsync` operation (the retry-attempt boundary), in a
  `catch` that rethrows bare so the original exception and its stack trace are preserved. Inert
  under strict one-scope-per-command hosting (the common ASP.NET Core request path); reachable
  wherever a scope outlives a single command — batch loops, scheduled jobs, Blazor Server circuits,
  integration tests chaining commands. Requires `MicroKit.Persistence.Abstractions` with
  `IUnitOfWork.DiscardChanges()` (ADR-005); no constructor change, no new dependency. See
  ADR-MEDIATR-012 for the call-site decision.

### Tests

**MicroKit.MediatR**
- `DomainEventsDispatcherRegistrationTests` becomes `DomainEventDispatchCompositionTests`
  (`MicroKit.MediatR.IntegrationTests`), re-purposed rather than deleted. With one dispatcher, the
  prior-registration stub no longer stands in for the Messaging glue — it stands in for a **consumer
  override**, which is still supported, so those tests keep their meaning under a new framing. All
  assertions still resolve through a real `ServiceProvider` rather than inspecting
  `ServiceDescriptor`s.

  It now also covers the sink seam — the batch is delivered once, after every handler has run for
  every event (the barrier); an empty batch invokes no sink; a throwing sink propagates — and the
  unreachable-notification guard, including its per-event precision: an event that maps to nothing
  must not throw merely because notifications exist elsewhere in the assembly.

  Recorded mutants: `IDomainEventsDispatcher` → `Add` and the concrete type → `Add` both survive the
  reframing and still fail. The two "still resolves" tests remain insensitive by design (`Add` and
  `TryAdd` are identical when nothing else is registered) and serve as regression guards. The new
  load-bearing mutant is **delete the sink loop from the orchestrator**, which fails 7 tests: 3 here
  and 4 in `MicroKit.Messaging.MediatR.IntegrationTests`.

**MicroKit.MediatR.Behaviors**
- Adds `TransactionBehaviorPersistenceTests` (`MicroKit.MediatR.IntegrationTests`) — the end-to-end
  proof for the two fixes above. The existing unit tests assert against an NSubstitute `IUnitOfWork`:
  they prove `TransactionBehavior` *calls* `DiscardChanges()`, not that a failed command's row stays
  out of the database. These three tests run the real chain — a real EF Core `DbContext` on SQLite
  with an open in-memory connection, the real `AddMicroKitPersistence` → `AddEntityFrameworkCore` →
  `AddDbContext` → `AddUnitOfWork<TContext>()` registrations, and `AddTransactionBehavior()` — with
  **one DI scope shared by two commands**, which is the topology that makes the defect reachable
  (`DbContext` is scoped, not per-command). A first command stages a row and then either returns
  `Result.Failure` or throws; a second command in the same scope succeeds and flushes; a fresh
  `DbContext` asserts only the second command's row exists. A positive control proves the harness
  actually writes, so the two absence assertions cannot pass vacuously. Mutation-verified: removing
  both discard call sites fails both defect tests, and the half-fix — keeping only the `IsFailure`
  discard — fails exactly the exception test. Composed without `MicroKit.Messaging`: the scenario
  raises no events, so the core `DomainEventDispatcher` runs unmodified. See ADR-MEDIATR-012.
  `MicroKit.MediatR.IntegrationTests` gains `Microsoft.EntityFrameworkCore`,
  `Microsoft.EntityFrameworkCore.Sqlite`, and a test-only `ProjectReference` to
  `MicroKit.Persistence.EntityFrameworkCore` — no production dependency edge is added.

### Documentation

**MicroKit.MediatR**
- Records **ADR-MEDIATR-013** — `IDomainEventsDispatcher` registration precedence as a cross-module
  contract: MicroKit.MediatR `TryAdd`s, MicroKit.Messaging.MediatR `Replace`s, and neither half is
  sufficient alone. Written to be readable from the Messaging side without MediatR context, and it
  spells out the silent failure mode of each half written the other way.
- The `AddMicroKitMediatR()` XML doc now states the "default, not an imposition" contract, that the
  dispatcher it supplies is outbox-free (P1 + P2 only), and that the Messaging.MediatR glue
  supersedes it.

**MicroKit.MediatR.Behaviors**
- Corrects the `[1.0.0-preview.2]` entry below, which described `TransactionBehavior` as wrapping
  the handler and dispatch "in a single database transaction via `ITransactionalContext`" with no
  mention of a flush. That description documented the defect rather than the intended behavior: a
  transaction alone writes nothing. The behavior requires `IUnitOfWork` as well, and the flush is a
  distinct step from the transaction commit.
- `TransactionBehavior` and `AddTransactionBehavior()` XML docs now disambiguate the two
  same-named calls — `IUnitOfWork.CommitAsync` is the flush (`SaveChangesAsync`);
  `IDbContextTransaction.CommitAsync` is the SQL commit, performed internally by
  `ITransactionalContext`. The behavior calls the first and never the second.
- The `TransactionBehavior` XML doc no longer claims that on a business failure "staged changes are
  discarded when the scope ends" — they were not, and that sentence described the defect fixed above.
  The execution sequence now names both non-commit exits explicitly, states why the exception path
  needs the discard *despite* the rollback, and records that nested command dispatch is unsupported
  (an inner command's failure would discard the outer command's staged work).

---

## [1.0.0-preview.2] — 2026-06-22

### Breaking Changes

**MicroKit.MediatR.Abstractions**
- `IDomainEventHandler<TEvent, TNotification>` renamed to `IDomainEventHandler<TEvent>` (single
  type parameter). Handlers no longer declare a notification type; the in-transaction dispatch
  path operates directly on the raw domain event. Remove the second generic parameter from all
  `IDomainEventHandler` implementations and change `Handle(TNotification n, …)` to
  `Handle(TEvent domainEvent, …)`.

**MicroKit.MediatR.Testing**
- `DomainEventTestHarness<TEvent, TNotification>` renamed to `DomainEventTestHarness<TEvent>`
  (single type parameter), consistent with the updated `IDomainEventHandler<TEvent>` contract.
  Pass the raw domain event to `HandleAsync(TEvent domainEvent, …)`.
- `FakeDomainEventDispatcher` now implements `IDomainEventsDispatcher` (`DispatchEventsAsync`)
  instead of the former `IDomainEventDispatcher` (`PublishAsync`). The published-events tracking
  surface (`PublishedEvents`, `AssertEventPublished<T>`, `GetSinglePublishedEvent<T>`,
  `AssertNoEventsPublished`) is replaced by dispatch-call tracking
  (`DispatchCallCount`, `AssertDispatchWasCalled`, `AssertDispatchCalledOnce`,
  `AssertDispatchNotCalled`). Update test assertions accordingly.

**MicroKit.MediatR (core)**
- `DomainEventHandlerAdapter` deleted. The adapter pattern is superseded by the new
  `IDomainEventHandlerDispatcher` + `HandlerDispatchMap` direct-dispatch mechanism.
- `IDomainEventsDispatcher` (plural, `DispatchEventsAsync`) is now the canonical dispatch
  interface. `IDomainEventDispatcher` (singular, `PublishAsync`) is `[Obsolete]` and will be
  removed in the next major version. Update injection points to `IDomainEventsDispatcher`.

### Added

**MicroKit.MediatR (core)**
- `IDomainEventHandlerDispatcher` — new scoped interface for direct, in-transaction dispatch to
  all registered `IDomainEventHandler<TEvent>` implementations. Bypasses the MediatR notification
  pipeline intentionally; invoked by `DomainEventDispatcher` during `DispatchEventsAsync`.
- `HandlerDispatchMap` — new singleton compiled-delegate map. Resolves `IDomainEventHandler<TEvent>`
  implementations at DI startup into pre-compiled Expression-tree delegates; O(1) per-event-type
  lookup with zero per-dispatch reflection.

**MicroKit.MediatR.Behaviors**
- `TransactionBehavior<TRequest, TResponse>` (pipeline order 700) — commands only
  (`ICommand` / `ICommand<TResult>`); opt-in via `AddTransactionBehavior()`. Wraps the command
  handler and subsequent domain event dispatch (`IDomainEventsDispatcher.DispatchEventsAsync`) in
  a single database transaction via `ITransactionalContext` (from `MicroKit.Persistence.Abstractions`).
  Skips event dispatch when the handler returns a business failure. Uses a static lambda and a
  `readonly struct` state-carrier for zero heap allocation per dispatch.
  > **Correction (see [Unreleased]):** this description is inaccurate as shipped. The behavior
  > wrapped the handler and dispatch in a transaction but never flushed — no `SaveChangesAsync` was
  > called, so every transactional command committed an empty transaction and wrote nothing. The
  > flush requires `IUnitOfWork`, which this version did not inject. Fixed in [Unreleased].

**MicroKit.MediatR.Abstractions**
- `PipelineOrder.Transaction = 700` added to the canonical pipeline order registry.
- `IDomainEventNotificationFactory` — supersedes `INotificationFactory` (now `[Obsolete]`).

### Changed

**MicroKit.MediatR.Abstractions**
- `INotificationFactory` is `[Obsolete]` — inject `IDomainEventNotificationFactory` in new code.
- `MicroKit.MediatR.Events.IEvent` is `[Obsolete]` — use `MicroKit.Domain.Events.IEvent` as the
  canonical MicroKit event root. The shim extends `MicroKit.Domain.Events.IEvent` for backward
  compatibility and will be removed in the next major version.
- `IDomainEventDispatcher` (singular) is `[Obsolete]` — use `IDomainEventsDispatcher` (plural).

### Dependencies

**MicroKit.MediatR.Behaviors**
- New production dependency on `MicroKit.Persistence.Abstractions` for `ITransactionalContext`
  (required by `TransactionBehavior`). The dependency is on the Abstractions layer only — no
  coupling to EF Core or any concrete ORM. See ADR-MEDIATR-011.

### Architecture

- ADRs ADR-MEDIATR-009 (single-param `IDomainEventHandler`), ADR-MEDIATR-010 (unified event
  taxonomy, `[Obsolete]` shims), and ADR-MEDIATR-011 (`Behaviors → Persistence.Abstractions`)
  accepted and implemented.

---

## [1.0.0-preview.1] — 2026-05-29

First public pre-release of MicroKit.MediatR.

### Added

**MicroKit.MediatR.Abstractions**
- `ICommand` / `ICommand<TResult>` — command contracts (mutate state, return unit or typed result)
- `IQuery<TResult>` — query contract (read-only, never mutates)
- `IStreamQuery<TResult>` — streaming query contract returning `IAsyncEnumerable<TResult>`
- `IEvent` — domain event marker; sealed records only, past-tense names enforced by convention
- `IDomainEventNotification<TEvent>` and `DomainEventNotification<TEvent>` — MediatR notification wrapper base
- `ICommandHandler<TCommand>` / `ICommandHandler<TCommand, TResult>` — handler contracts returning `ValueTask`
- `IQueryHandler<TQuery, TResult>` — handler contract returning `ValueTask<TResult>`
- `IStreamQueryHandler<TQuery, TResult>` — handler contract returning `IAsyncEnumerable<TResult>`
- `IDomainEventHandler<TEvent, TNotification>` — notification handler contract returning `Task`
- Behavior opt-in markers: `IAuthorizedRequest`, `IIdempotentCommand`, `ICacheableQuery`, `IRetryableRequest`
- `ICurrentUserAccessor` — decoupled user context for authorization (no `IHttpContextAccessor` dependency)

**MicroKit.MediatR (core)**
- `BehaviorBase<TRequest, TResponse>` — mandatory base class for all pipeline behaviors; caches `Result<T>` detection and failure construction per closed generic (zero per-request reflection)
- `PipelineOrder` — canonical order registry: Logging=100, Authorization=200, Validation=300, Idempotency=400, Caching=500, Retry=600
- `IDomainEventDispatcher.PublishAsync` — domain event dispatch without `IMediator` coupling; O(1) lookup via registration-time compiled factory (ADR-005)
- `DomainEventDispatcher` — production implementation; enforces one-notification-per-event-type at DI startup
- Handler adapters: `CommandHandlerAdapter`, `QueryHandlerAdapter`, `StreamQueryHandlerAdapter`, `DomainEventHandlerAdapter` — bridge MicroKit's `ValueTask`-based contracts to MediatR's `Task`-based internals with sync fast-path (zero `Task` box on synchronous completion)
- `MediatRBuilder` — fluent DI registration with assembly scanning, behavior validation, and domain event factory construction
- `AddMicroKitMediatR()` extension — single-call registration entry point
- `SendCommandAsync`, `SendQueryAsync`, `StreamQueryAsync` — typed `IMediator` extension methods

**MicroKit.MediatR.Behaviors**
- `LoggingBehavior` (order 100) — always active; structured logging via `LogPropertyNames.CommandName`; source-generated `[LoggerMessage]` delegates; OpenTelemetry `Activity` tracing; never short-circuits
- `AuthorizationBehavior` (order 200) — opt-in via `IAuthorizedRequest`; ASP.NET Core policy evaluation; produces `UnauthenticatedError` / `UnauthorizedError`; fail-fast before validation
- `ValidationBehavior` (order 300) — opt-in via registered `IValidator<T>`; collect-all: runs all validators, aggregates all `ValidationFailure`s into a single `ValidationError`; zero-cost pass-through when no validators registered
- `IdempotencyBehavior` (order 400) — opt-in via `IIdempotentCommand`; commands only; `IIdempotencyStore` abstraction with `DistributedCacheIdempotencyStore` default; never caches `Result.Failure`
- `CachingBehavior` (order 500) — opt-in via `ICacheableQuery`; queries only; `IDistributedCache` + STJ deserialization; never caches `Result.Failure`; warns on null `Expiry`
- `RetryBehavior` (order 600) — opt-in via `IRetryableRequest`; Polly `ResiliencePipeline` cached per request type; exponential back-off with jitter; retries only transient exceptions
- `ResultInspector<TResponse>` — cached per-closed-generic `Result<T>` detection; eliminates per-request reflection across all behaviors
- Error types: `ValidationError`, `UnauthenticatedError`, `UnauthorizedError`, `CacheDeserializationError`
- `IIdempotencyStore` / `DistributedCacheIdempotencyStore` — idempotency storage abstraction

**MicroKit.MediatR.Testing**
- `CommandHandlerTestHarness<TCommand, TResult>` — factory constructor wires `FakeDomainEventDispatcher` for `AssertEventPublished<T>` support; direct constructor for event-free handlers
- `CommandHandlerTestHarness<TCommand>` — void-command variant with identical event tracking surface
- `QueryHandlerTestHarness<TQuery, TResult>` — direct handler wrapping via `QueryAsync`
- `StreamQueryHandlerTestHarness<TQuery, TResult>` — direct handler wrapping via `StreamAsync`
- `DomainEventTestHarness<TEvent, TNotification>` — direct notification delivery via `HandleAsync`
- `BehaviorTestHarness<TRequest, TResponse>` — `ExecuteAsync` with fixed-result or custom `next` delegate; `NextWasCalled` / `NextCallCount` for marker-guard assertions
- `FakeDomainEventDispatcher` — standalone `IDomainEventDispatcher` fake with `PublishedEvents`, `AssertEventPublished<T>`, `AssertNoEventsPublished`, `GetSinglePublishedEvent<T>`, `Reset`

**CI/CD**
- `ci-mediatr.yml` — path-filtered CI workflow for PRs and pushes to `modules/MicroKit.MediatR/**`
- `release-mediatr.yml` — tag-triggered release; `CIReleaseBuild=true` substitutes cross-module `ProjectReference`s with published NuGet `PackageReference`s; version extracted from tag name

### Architecture

- 8 accepted ADRs (ADR-001 through ADR-008) governing Result dependency, BehaviorBase mandate, ValueTask policy, opt-in behaviors, domain event factory, authorization decoupling, JSON serialization caveat, and ICurrentUserAccessor placement
- Full NativeAOT/trimming annotations on all reflection-using registration paths
- 135 tests across UnitTests, IntegrationTests, ArchitectureTests, and PerformanceTests

---

[1.0.0-preview.2]: https://github.com/michaelatsey/MicroKit/releases/tag/mediatr-v1.0.0-preview.2
[1.0.0-preview.1]: https://github.com/michaelatsey/MicroKit/releases/tag/mediatr-v1.0.0-preview.1
