# Changelog — MicroKit.MediatR

All notable changes to this package are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) — [Semantic Versioning](https://semver.org/).

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

[1.0.0-preview.1]: https://github.com/michaelatsey/MicroKit/releases/tag/mediatr-v1.0.0-preview.1
