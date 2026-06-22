# Changelog — MicroKit.MediatR

All notable changes to this package are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) — [Semantic Versioning](https://semver.org/).

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
