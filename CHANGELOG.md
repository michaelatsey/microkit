# Changelog

All notable changes to MicroKit will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

## [1.0.0-preview.3] - 2026-05-14

### Added

- `MicroKit.Security` — JWT, API key, Azure AD, Cognito, ASP.NET Core, and multi-tenancy security providers
- `MicroKit.Resilience` — retry, circuit breaker, timeout strategies with MediatR pipeline integration
- `MicroKit.OpenApi` — Scalar UI, versioning, operation filters, and document transformers
- `Samples/OrderApi` — Hexagonal + DDD + CQRS reference sample using MicroKit NuGet packages v1.0.0-preview.2

### Removed

- `MicroKit.Payments` — deferred indefinitely; removed from solution and repository

## [1.0.0-preview.2] - 2026-05-13

### Added

- README for all 24 packages (`docs/Readme.md` per module, wired via `PackageReadmeFile`)

### Fixed

- NuGet packages now published with the correct version derived from git tag
- `MicroKit.Caching.Abstractions` was missing from the solution file
- `PackageReadmeFile` scoped to modules that have a `docs/Readme.md`; non-doc projects no longer emit a warning

## [1.0.0-preview.1] - 2026-05-12

### Added

- `MicroKit.Domain.Abstractions` — `Entity<TKey>`, `AggregateRootBase`, `ValueObject`, `Enumeration`, `IDomainEvent`, audit interfaces
- `MicroKit.Domain` — `Result<T>`, `Error`, `Money` (ISO 4217), `AuditedEntity`, `AuditedAggregateRoot`
- `MicroKit.Cqrs.Abstractions` — `ICommand`, `IQuery<T>`, `ICommandBus`, `IQueryBus`, handler interfaces, cache contracts
- `MicroKit.Cqrs.MediatR` — `MediatRCommandBus`, `MediatRQueryBus`
- `MicroKit.Cqrs.MediatR.Behaviors` — `LoggingBehavior`, `ValidationBehavior`, `PerformanceBehavior`
- `MicroKit.Cqrs.MediatR.Caching` — `CachingBehavior`, `CacheInvalidationBehavior`
- `MicroKit.Events.Abstractions` — `IDomainEvent`, `IIntegrationEvent`, `IEvent`
- `MicroKit.Events` — `EventBase` with 6-field tracing contract
- `MicroKit.Messaging.Abstractions` — `IOutboxService`, `IInboxHandler<T>`, `OutboxMessage`, `InboxMessage`, `InboxState`
- `MicroKit.Messaging.Core` — outbox/inbox background workers, cleanup workers, `MessagingValidationService`
- `MicroKit.Messaging.Persistence.EFCore` — EF Core repositories, 3-phase `OptimisticInboxLockingStrategy`
- `MicroKit.Messaging.Transport.RabbitMQ` — RabbitMQ `IMessageTransport`
- `MicroKit.Idempotency.Abstractions` — `IIdempotentRequest<T>`, `IIdempotencyStore`, `IRequestHasher`
- `MicroKit.Idempotency.Core` — `IdempotencyProvider`, SHA-256 `RequestHasher`, `IdempotencyCleanupWorker`
- `MicroKit.Idempotency.EFCore` — `EfCoreIdempotencyStore` (stages in ChangeTracker, never calls `SaveChangesAsync`)
- `MicroKit.Idempotency.Redis` — Redis-backed idempotency store
- `MicroKit.Idempotency.MediatR` — `IdempotencyBehavior` (4-state machine)
- `MicroKit.Data.Abstractions` — `IRepository<T>`, `IReadRepository<T>`, `IUnitOfWork`, `ITransactionalContext`
- `MicroKit.Data.EntityFrameworkCore` — `EfUnitOfWork<TDbContext>`
- `MicroKit.EntityFrameworkCore` — `JsonValueConverters`, model builder extensions
- `MicroKit.MultiTenancy.Abstractions` — `ITenantContext`, `ITenant`, `ITenantResolutionStrategy`, `IHasMultiTenant`
- `MicroKit.MultiTenancy` — `TenantContext` (write-once), `CompositeTenantRegionResolver`, startup DI validator
- `MicroKit.MultiTenancy.Extensions` — `TenantResolutionMiddleware`, `HeaderResolutionStrategy`, `JwtClaimResolutionStrategy`
- `MicroKit.MultiTenancy.EFCoreStore` — `EFCoreTenantStore`
- `MicroKit.MultiTenancy.Redis` — `RedisTenantCache`
- `MicroKit.Caching.Abstractions` — `ICacheService`, `CacheOptions`
- `MicroKit.Caching.Distributed` — `DistributedCacheService`, `AddMicroKitDistributedCache`
- `MicroKit.Caching.Distributed.Autofac` — Autofac registration extension

[Unreleased]: https://github.com/michaelatse/microkit/compare/v1.0.0-preview.3...HEAD
[1.0.0-preview.3]: https://github.com/michaelatse/microkit/compare/v1.0.0-preview.2...v1.0.0-preview.3
[1.0.0-preview.2]: https://github.com/michaelatse/microkit/compare/v1.0.0-preview.1...v1.0.0-preview.2
[1.0.0-preview.1]: https://github.com/michaelatse/microkit/releases/tag/v1.0.0-preview.1
