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

### Changed

**MicroKit.MediatR**
- `AddMicroKitMediatR()` now registers `IDomainEventsDispatcher` with `TryAdd` instead of `Add`. Its
  contract changes from *imposes a dispatcher* to *provides a default*: if a higher-level module has
  already registered one, that registration is kept. This makes the two implementations that exist
  by design (ADR-MEDIATR-010) order-independent. Previously both `AddMicroKitMediatR()` and
  `AddMediatRTransport()` used plain `AddScoped`, so the winner was whichever ran last — and
  `AddMediatRTransport()` before `AddMicroKitMediatR()` silently resolved the core dispatcher, which
  runs domain-event handlers but writes **nothing** to the transactional outbox: no exception, no
  log, no startup error, just an outbox that never fills. The same change applies to the concrete
  backing registration and to the `[Obsolete]` `IDomainEventDispatcher` alias — the rule covers every
  dispatcher descriptor the method registers, with no exceptions. A side effect: calling
  `AddMicroKitMediatR()` twice no longer appends duplicate dispatcher descriptors.
  **Migration:** a consumer who deliberately overrode the core dispatcher by registering their own
  *after* `AddMicroKitMediatR()` is unaffected (a later `Add` still wins). A consumer who registered
  their own *before* it now keeps theirs, where previously it was overridden. To override
  unconditionally regardless of order, use `Replace` — the same API the Messaging glue uses.

  **The precedence rule, in full** (ADR-MEDIATR-013 records the decision; the rule itself is stated
  here so it is available without the repository):

  | Package | Method | Registers with | Meaning |
  |---------|--------|----------------|---------|
  | `MicroKit.MediatR` | `AddMicroKitMediatR()` | `TryAdd` | Supplies a *default*; abstains if the slot is taken |
  | `MicroKit.Messaging.MediatR` | `AddMediatRTransport()` | `Replace` | Supplies the *authoritative* implementation; takes the slot unconditionally |

  With `TryAdd` on one side and `Replace` on the other, both call orders converge on the glue
  implementation: core-then-glue, the core registers and the glue replaces it; glue-then-core, the
  glue registers and the core finds the slot taken and abstains. Neither half is sufficient alone —
  an `Add` on either side reintroduces the silent order dependency described above. The glue half
  ships separately in `MicroKit.Messaging`; until it does, core-then-glue continues to work by
  position and glue-then-core is already fixed by this change alone.

**MicroKit.MediatR.Behaviors**
- No API change, but `TransactionBehavior` is the package's consumer of `IDomainEventsDispatcher`
  (constructor parameter 2), so *which* dispatcher your pipeline runs is now decided by the `TryAdd`
  precedence rule above rather than by DI registration order. Behaviour is unchanged for the
  documented registration order; it changes only where `AddMediatRTransport()` ran before
  `AddMicroKitMediatR()`, which previously left the transaction dispatching domain events without
  ever staging an outbox row. All four packages are co-versioned and ship together — see the
  **MicroKit.MediatR** entry above for the full rule.

**MicroKit.MediatR.Testing**
- No API change. `FakeDomainEventDispatcher` implements `IDomainEventsDispatcher`, so registering it
  in a container *before* `AddMicroKitMediatR()` now takes effect, where previously the real
  dispatcher silently overwrote it. Its primary documented use — direct construction and injection
  into a behavior under test — is unaffected.

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
- Adds `DomainEventsDispatcherRegistrationTests` (`MicroKit.MediatR.IntegrationTests`) — five tests
  for the `TryAdd` contract above, all exercised through a real `ServiceProvider` rather than by
  inspecting `ServiceDescriptor`s. They cover: the core dispatcher resolving when
  `AddMicroKitMediatR()` is called alone; a prior registration surviving the call (the test that
  reproduces the original defect, and the reason the glue-then-core order becomes safe); no
  duplicate descriptors on a second call; and the concrete dispatcher still resolving, both with and
  without a prior interface registration. Because MicroKit.MediatR cannot reference
  MicroKit.Messaging, a stub registered beforehand stands in for `AddMediatRTransport()` — faithful,
  since `TryAdd` matches on service type alone. Mutation-verified per descriptor: reverting any one
  of the three `TryAdd` calls to `Add` fails at least one test, and each failure is attributable to
  its own descriptor. The two "still resolves" tests are insensitive by design — `Add` and `TryAdd`
  are identical when nothing else is registered — and serve as regression guards that the default is
  still supplied and the concrete descriptor is never skipped.

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
