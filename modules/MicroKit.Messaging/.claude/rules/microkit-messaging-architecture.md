# microkit-messaging-architecture

## Always active for every file in this module.

## Layer Boundaries

### Abstractions (`MicroKit.Messaging.Abstractions`)
- Zero dependency on ASP.NET Core, EF Core, or any broker package
- Only allowed deps: `MicroKit.Result`, BCL types — no `MicroKit.Domain.Abstractions` (ADR-MSG-001)
- All contracts are interfaces; entities are `sealed class`; VOs are `sealed record`
- Contains: `IIntegrationEvent`, `IMessagePublisher`, `IMessageHandler<T>`,
  `IOutboxWriter`, `IOutboxProcessorStore`, `IInboxStore`,
  `OutboxMessage` (sealed class), `InboxMessage` (sealed class), `MessageEnvelope<T>` (sealed record),
  `MessageId`, `CorrelationId`, `CausationId`, `OutboxMessageStatus`, `InboxMessageStatus`
- **`IOutboxWriter` and `IOutboxProcessorStore` live here** — never in `MicroKit.Persistence.Abstractions`
- `OutboxMessage` and `InboxMessage` are `sealed class` with `{ get; set; }` — EF Core entities that require mutable properties
- No implementation logic

### Core (`MicroKit.Messaging`)
- Depends on `MicroKit.Messaging.Abstractions` only (no broker, no EF Core)
- Contains: `InProcessMessagePublisher`, `MessageDispatcher` (internal sealed class),
  `OutboxProcessor`, `InboxProcessor`, `MessagingBuilder`, DI extensions
- **No broker coupling** — the core compiles without any broker NuGet package
- Background processors are `BackgroundService` (IHostedService) — `IServiceScopeFactory` only
- DI registration via `AddMicroKitMessaging()` extension on `IServiceCollection`
- `MessageDispatcher` is `internal` to Core — never exposed as a public interface

### EntityFrameworkCore (`MicroKit.Messaging.EntityFrameworkCore`)
- Depends on `MicroKit.Messaging` + `MicroKit.Persistence.EntityFrameworkCore`
- Contains: `EfOutboxStore`, `EfInboxStore`, EF entity configurations, migration helpers
- EF Core types confined here — never leak into Core or Abstractions
- DI extension: `AddEfCoreOutbox()` / `AddEfCoreInbox()` on `MessagingBuilder`

### Testing (`MicroKit.Messaging.Testing`)
- Depends on `MicroKit.Messaging.Abstractions` only — never on Core or EF Core
- Contains: `FakeMessagePublisher`, `InMemoryOutboxStore`, `InMemoryInboxStore`,
  `ShouldHavePublished<T>()`, `ShouldHaveConsumed<T>()` assertion helpers
- Test doubles implement Abstractions interfaces directly — no framework dependency

### Serialization (`MicroKit.Messaging.Serialization` — v2)
- Depends on `MicroKit.Messaging.Abstractions` only
- Contains `IMessageSerializer` interface + `SystemTextJsonMessageSerializer` default implementation
- v1 Core uses `System.Text.Json` directly for serialization (before this package exists)
- The extension point `IMessageSerializer` must be defined in Core for v1 providers to use

### v2 Provider Packages (`RabbitMQ`, `AzureServiceBus`, `Kafka`)
- Each depends on `MicroKit.Messaging` (Core) + the provider's broker client package
- Provider packages **never depend on each other** — they are siblings
- Each implements `IMessagePublisher` for outbound + broker-specific consumer registration
- `IsPackable=false` until Phase 2 implementation is complete

---

## Forbidden Patterns

```
❌ IOutboxWriter or IOutboxProcessorStore in MicroKit.Persistence.Abstractions
❌ IOutboxStore as a single interface — ISP violation; use IOutboxWriter + IOutboxProcessorStore
❌ IIntegrationEvent derived from INotification or any MediatR type
❌ MediatR.Contracts referenced in any package (including tests)
❌ Broker client package (RabbitMQ.Client, Azure.Messaging.ServiceBus, Confluent.Kafka)
   referenced in MicroKit.Messaging (Core) or Abstractions
❌ IHttpContextAccessor used in OutboxProcessor or InboxProcessor
❌ Scoped service captured in IHostedService field — always use IServiceScopeFactory
❌ TenantId absent from OutboxMessage or InboxMessage
❌ Publisher returning success when underlying publisher is null — throw InvalidOperationException
❌ Console.WriteLine — use ILogger<T>
❌ Circular dependency between any two packages
❌ FluentAssertions — use Shouldly (MIT)
```

---

## Background Processor Rules

```
OutboxProcessor and InboxProcessor rules:
  ✅ Extend BackgroundService
  ✅ Constructor injects IServiceScopeFactory and ILogger<T> only
  ✅ Create IAsyncServiceScope per MESSAGE (not per batch) — isolation mandatory
  ✅ Resolve IOutboxProcessorStore/IInboxStore from scope — never from constructor
  ✅ TenantId propagated from OutboxMessage/InboxMessage into processing context
  ✅ CancellationToken threaded through all async calls
  ✅ Lease acquired atomically via AcquireLeaseAsync (single ExecuteUpdateAsync WHERE)
  ✅ Failed messages marked with error message — not silently discarded
  ✅ TenantId read from OutboxMessage.TenantId / InboxMessage.TenantId — never IHttpContextAccessor

  ❌ Constructor injection of scoped services (IOutboxProcessorStore, IMessagePublisher, etc.)
  ❌ Sharing one IAsyncServiceScope across multiple messages in a batch
  ❌ IHttpContextAccessor referenced anywhere in processors
  ❌ static field used to share state between processor instances
  ❌ SELECT + foreach mutate + SaveChanges for lease acquisition (not atomic)
```

---

## What Belongs Where

| Concern | Package |
|---------|---------|
| `IIntegrationEvent` contract | Abstractions |
| `IOutboxWriter` contract | Abstractions |
| `IOutboxProcessorStore` contract | Abstractions |
| `IInboxStore` contract | Abstractions |
| `OutboxMessage` entity (sealed class) | Abstractions |
| `InboxMessage` entity (sealed class) | Abstractions |
| `MessageEnvelope<T>` | Abstractions |
| `MessageId`, `CorrelationId`, `CausationId` | Abstractions |
| `InProcessMessagePublisher` | Core |
| `OutboxProcessor` (background worker) | Core |
| `InboxProcessor` (background worker) | Core |
| `MessageDispatcher` | Core |
| DI extensions (`AddMicroKitMessaging`) | Core |
| `EfOutboxStore` | EntityFrameworkCore |
| `EfInboxStore` | EntityFrameworkCore |
| EF entity configurations | EntityFrameworkCore |
| `FakeMessagePublisher` | Testing |
| `InMemoryOutboxStore` | Testing |
| `InMemoryInboxStore` | Testing |
| `ShouldHavePublished<T>` assertions | Testing |
| `RabbitMqMessagePublisher` | RabbitMQ (v2) |
| `AzureServiceBusPublisher` | AzureServiceBus (v2) |
| `KafkaMessagePublisher` | Kafka (v2) |
| `IMessageSerializer` contract | Core (v1 default; Serialization package adds Avro/Protobuf) |
| `SystemTextJsonMessageSerializer` | Core |
