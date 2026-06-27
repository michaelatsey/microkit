# Changelog — MicroKit.Messaging

## [1.0.0-preview.4] — 2026-06-27

### Packages Released
- `MicroKit.Messaging.Abstractions`
- `MicroKit.Messaging`
- `MicroKit.Messaging.EntityFrameworkCore`
- `MicroKit.Messaging.MediatR`

### Added
#### MicroKit.Messaging.Abstractions
- `IIntegrationEvent` — typed integration event contract (standalone, no Domain dependency — ADR-MSG-001)
- `IMessagePublisher` — outbound message publishing contract
- `IMessageHandler<T>` — inbound message handling contract
- `IOutboxWriter` — write-side outbox contract (`AddBatchAsync`)
- `IOutboxProcessorStore` — processor-side outbox contract (lease, ack, dead-letter)
- `IInboxStore` — inbox deduplication contract
- `OutboxMessage` / `InboxMessage` — sealed classes (EF Core mutable)
- `MessageEnvelope<T>` — sealed record
- `MessageId`, `CorrelationId`, `CausationId` — strong-typed value objects (sealed record)
- `OutboxMessageStatus` / `InboxMessageStatus` — enums

#### MicroKit.Messaging
- `OutboxMessageFactory` — builds `OutboxMessage` from integration events
- `InProcessMessagePublisher` — in-process synchronous publisher
- `InProcessIntegrationDispatcher` — in-process integration event dispatcher
- `OutboxProcessor` / `InboxProcessor` — polling processors (ADR-MSG-002)
- `IMessageSerializer` / `SystemTextJsonMessageSerializer` — JSON serialization (ADR-MSG-003)
- `IExecutionScopeFactory` integration via `MicroKit.Execution.Abstractions` (ADR-EXEC-001)
- DI extensions

#### MicroKit.Messaging.EntityFrameworkCore
- `EfOutboxStore<TContext>` — `IOutboxWriter` + `IOutboxProcessorStore` (atomic lease via `ExecuteUpdateAsync`, stale-lease recovery, dead-letter — ADR-MSG-002)
- `EfInboxStore<TContext>` — `IInboxStore` (dedup via unique constraint + `DbUpdateException`)
- `OutboxMessageConfiguration` / `InboxMessageConfiguration` — EF Core entity configs
- `ModelBuilderExtensions.ApplyMessagingConfiguration()`
- `MessagingBuilderExtensions.AddEfCoreOutbox<TContext>()`

#### MicroKit.Messaging.MediatR
- `DomainEventsDispatcher` — dispatches domain events to handlers and outbox (ADR-MEDIATR-009)
- `IDomainEventNotificationFactory` — creates `IDomainEventNotification<T>` from domain events
- `IDomainEventHandlerDispatcher` — dispatches to `IDomainEventHandler<T>` (sync, in-transaction)
- MediatR glue for outbox-based async dispatch (P4 pipeline)

### Notes
- `preview.1` through `preview.3` were taken on NuGet.org by a previous implementation
- This is the first release of the new implementation
