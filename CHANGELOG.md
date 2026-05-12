# Changelog

All notable changes to MicroKit will be documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/).

---

## [1.0.0-preview.1] — 2026-05-12

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
