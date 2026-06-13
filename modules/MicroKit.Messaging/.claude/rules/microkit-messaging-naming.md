# microkit-messaging-naming

## General Rules

- `sealed record` — value objects, events, messages, options, envelopes
- `sealed class` — services, publishers, handlers, processors, dispatchers, stores
- `interface` prefix `I` — all contracts in Abstractions
- No `Base` suffix — use composition, not inheritance
- No `Helper`, `Utils`, `Manager` suffix — name by responsibility

---

## Core Contracts

| Pattern | Example |
|---------|---------|
| `IIntegrationEvent` | typed contract — all integration events implement this; defines TenantId, CorrelationId, CausationId, OccurredOnUtc |
| `IMessagePublisher` | publishes outbound messages to broker or in-process |
| `IMessageHandler<T>` | handles a specific integration event type |
| `IOutboxWriter` | write-only outbox access for domain handlers (AddAsync only) |
| `IOutboxProcessorStore` | read/write outbox access for background processor (GetPendingAsync, AcquireLeaseAsync, MarkPublishedAsync, ...) |
| `IInboxStore` | read/write access to the inbox table (dedup + processing state) |
| `MessageDispatcher` | `internal sealed class` in Core — routes to `IMessageHandler<T>`; NOT a public Abstractions contract |

---

## Value Objects and Records

| Pattern | Example |
|---------|---------|
| `MessageId` | strongly-typed message identifier — `sealed record MessageId(Guid Value)` |
| `CorrelationId` | correlation chain identifier — `sealed record CorrelationId(Guid Value)` |
| `CausationId` | causal parent identifier — `sealed record CausationId(Guid Value)` |
| `OutboxMessage` | outbox EF Core entity — `sealed class OutboxMessage` with `{ get; set; }` |
| `InboxMessage` | inbox EF Core entity — `sealed class InboxMessage` with `{ get; set; }` |
| `MessageEnvelope<T>` | wraps `T : IIntegrationEvent` with routing metadata — `sealed record` |
| `{Name}Options` | configuration record — `MessagingOptions`, `OutboxProcessorOptions` |

> `OutboxMessage` and `InboxMessage` are `sealed class` (not `sealed record`) because
> EF Core change tracking requires mutable `{ get; set; }` properties.

---

## Outbox / Inbox State

| Value | Meaning |
|-------|---------|
| `OutboxMessageStatus.Pending` | written, not yet dispatched; eligible for lease |
| `OutboxMessageStatus.Processing` | lease acquired, in-flight (`LockedUntilUtc > now`) |
| `OutboxMessageStatus.Published` | confirmed delivery — terminal |
| `OutboxMessageStatus.Failed` | **always terminal** — max retries exceeded, `DeadLettered=true`; never used for transient failures |
| `InboxMessageStatus.Received` | received, not yet processed |
| `InboxMessageStatus.Processing` | handler executing (lease held) |
| `InboxMessageStatus.Processed` | handler completed successfully — terminal |
| `InboxMessageStatus.Failed` | handler failed, retry pending |

> **`OutboxMessageStatus.Failed` = permanent, terminal.** Failed attempts reset to `Pending`
> (not to `Failed`). The `Failed` status is only set by `DeadLetterAsync` when
> `RetryCount >= MaxRetries`. `DeadLettered=true` is always set simultaneously.

---

## Implementations

| Pattern | Example |
|---------|---------|
| `InProcess{Noun}` | `InProcessMessagePublisher` — in-process default |
| `{Provider}{Noun}` | `RabbitMqMessagePublisher`, `AzureServiceBusPublisher` |
| `Ef{Noun}` | `EfOutboxStore`, `EfInboxStore` — EF Core implementations |
| `{Noun}Processor` | `OutboxProcessor`, `InboxProcessor` — background workers |
| `{Noun}Dispatcher` | `MessageDispatcher` — internal routing |
| `Fake{Noun}` | `FakeMessagePublisher` (Testing package only) |
| `InMemory{Noun}` | `InMemoryOutboxStore`, `InMemoryInboxStore` (Testing package only) |

---

## Integration Events

```csharp
// ✅ Named by domain fact — past tense, domain language
public sealed record OrderPlacedEvent : IIntegrationEvent { ... }
public sealed record UserRegisteredEvent : IIntegrationEvent { ... }
public sealed record InventoryReservedEvent : IIntegrationEvent { ... }

// ❌ Wrong naming
public sealed record OrderMessage { ... }           // ← not an event, generic noun
public sealed record OnOrderPlaced { ... }          // ← on- prefix is wrong
public class OrderPlacedNotification { ... }        // ← Notification = MediatR coupling
```

---

## DI Extension Methods

| Pattern | Example |
|---------|---------|
| `AddMicroKitMessaging()` | on `IServiceCollection` — main registration entry point |
| `AddEfCoreOutbox()` | on `MessagingBuilder` — wires `EfOutboxStore` (implements both `IOutboxWriter` + `IOutboxProcessorStore`) + `EfInboxStore` |
| `AddInProcessTransport()` | on `MessagingBuilder` — wires `InProcessMessagePublisher` |
| `Add{Provider}Transport()` | on `MessagingBuilder` — wires a broker provider (e.g., `AddRabbitMqTransport()`) |
| `AddMessageHandler<THandler, TEvent>()` | on `MessagingBuilder` — registers a handler |

---

## Test Method Naming

```
{Method}_{Scenario}_{ExpectedResult}

Examples:
  PublishAsync_WhenEventValid_StoresInOutbox
  PublishAsync_WhenPublisherNull_ThrowsInvalidOperation
  GetPendingAsync_WhenMessagesExist_ReturnsBatch
  MarkPublishedAsync_WhenMessageNotFound_ReturnsFailure
  ExistsAsync_WhenMessageAlreadyProcessed_ReturnsTrue (dedup gate)
  HandleAsync_WhenAlreadyProcessed_SkipsHandler (idempotency)
```

---

## Files

| Type | Convention | Example |
|------|-----------|---------|
| Interface | `I{Name}.cs` | `IOutboxStore.cs`, `IMessagePublisher.cs` |
| Implementation | `{Name}.cs` | `EfOutboxStore.cs`, `OutboxProcessor.cs` |
| Options | `{Name}Options.cs` | `OutboxProcessorOptions.cs` |
| Extensions | `{Name}Extensions.cs` | `ServiceCollectionExtensions.cs` |
| Tests | `{Name}Tests.cs` | `OutboxProcessorTests.cs` |
| EF config | `{Entity}Configuration.cs` | `OutboxMessageConfiguration.cs` |

---

## Prefixes and Patterns to Avoid

---

## Serialization

| Pattern | Example |
|---------|---------|
| `IMessageSerializer` | `Serialize(MessageEnvelope<T>)` → `string`; `Deserialize<T>(string)` → `MessageEnvelope<T>` |
| `SystemTextJsonMessageSerializer` | default v1 implementation using `System.Text.Json` |

---

## Prefixes and Patterns to Avoid

```
❌ MessageHelper, MessageUtils, MessagingManager
❌ BaseMessageHandler, AbstractPublisher
❌ INotification (MediatR type) — use IIntegrationEvent
❌ Notification suffix on events — it implies MediatR
❌ On{EventName} — use {EventName}Handler : IMessageHandler<{EventName}Event>
```
