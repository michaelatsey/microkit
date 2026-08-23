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
| `IOutboxWriter` | write-only outbox access for domain handlers — `AddAsync` (single) + `AddBatchAsync` (ADR-MSG-011, the path `OutboxDomainEventSink` uses) |
| `IOutboxProcessorStore` | claim + settlement for the background processor — `ClaimBatchAsync`, `ApplyOutcomesAsync`. The per-message lease API (`GetPendingAsync`, `AcquireLeaseAsync`, `MarkPublishedAsync`, `MarkFailedAsync`, `DeadLetterAsync`) was removed by the outbox claim rewrite |
| `IOutboxAdminStore` | dead-letter inspection and requeue for operator tooling only — `GetDeadLetteredAsync`, `RequeueAsync` |
| `IOutboxRetentionStore` | retention for the cleanup worker only — `DeleteProcessedAsync` |
| `IInboxWriter` | inbox ingestion — `ExistsAsync`, `AddAsync`. `AddAsync` returns `InboxWriteResult`: a redelivery is reported through the return value, never thrown (ADR-MSG-017) |
| `IInboxProcessorStore` | claim + deferred settlement for the drain processor — `ClaimBatchAsync`, `ApplyOutcomesAsync`. The per-message lease API (`GetPendingAsync`, `MarkProcessingAsync`, `MarkProcessedAsync`, `MarkFailedAsync`, `DeadLetterAsync`) was removed by the inbox claim rewrite |
| `IInboxSettlementStore` | `StageProcessedAsync`, `IsMarkUncommitted`, `IsLeaseLost` — resolved from the **per-message** execution scope so the processed mark commits in the handler's own transaction. Stages only; never calls `SaveChangesAsync` |
| `IInboxAdminStore` | dead-letter inspection and requeue for operator tooling only — `GetDeadLetteredAsync`, `RequeueAsync` |
| `IInboxRetentionStore` | retention for the inbox cleanup worker only — `DeleteProcessedAsync` |
| `IOutboxDispatcher` | the dispatch seam in Core — deserializes an `OutboxMessage` and routes it. **Replaced the former `MessageDispatcher`**, which no longer exists and must not be re-introduced (pinned by `Core_DoesNotContainTypeNamedMessageDispatcher`) |

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
| `InboxMessageStatus.Received` | received, not yet processed. Reached from three places with three meanings — retried, released, or requeued — and only one costs a retry |
| `InboxMessageStatus.Processing` | claimed; lease held until `LockedUntilUtc`, `ClaimToken` names the owner |
| `InboxMessageStatus.Processed` | handler completed successfully — terminal |
| `InboxMessageStatus.Failed` | **always terminal** — permanently unprocessable or past `MaxRetries`, `DeadLettered=true` set simultaneously |

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
| `{Noun}Processor` | `OutboxProcessor`, `InboxProcessor` — topology-agnostic batch engines |
| `{Noun}Worker` | `OutboxWorker`, `InboxWorker`, `OutboxRetentionWorker`, `InboxRetentionWorker` — `BackgroundService` hosts. `internal sealed`; only `IServiceScopeFactory` is injected |
| `{Noun}Dispatcher` | `InProcessIntegrationDispatcher`, `MediatROutboxDispatcher` — `IOutboxDispatcher` implementations, `internal sealed`. NOT `MessageDispatcher`: that type was eliminated in favour of the `IOutboxDispatcher` seam and its re-introduction is blocked by an architecture test |
| `{Noun}Sink` | `OutboxDomainEventSink` — an `IDomainEventsSink` (MicroKit.MediatR.Abstractions) contributed to the core domain-event orchestrator. `internal sealed`, registered with `TryAddEnumerable` so the collection dedups on implementation type. A sink **contributes** to a sequence it does not own; a `{Noun}Dispatcher` **owns** one. Never register a sink as a rival dispatcher (ADR-MSG-016) |
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
| `AddEfCoreOutbox()` | on `MessagingBuilder` — wires `EfOutboxStore` and `EfInboxStore`. Each is registered **once as scoped by concrete type**, with every interface resolving to that instance through a factory lambda, so one scope holds one store over one `DbContext`. That is also what makes `IInboxSettlementStore` transactional: resolved from the per-message execution scope it necessarily shares its `TContext` with the handler resolved from the same scope. Registering either store as anything other than scoped breaks the guarantee silently. The name is now a misnomer — it wires the inbox too |
| `AddInProcessTransport()` | on `MessagingBuilder` — wires `InProcessMessagePublisher` |
| `Add{Provider}Transport()` | on `MessagingBuilder` — **broker providers ONLY** (e.g. `AddRabbitMqTransport()`). This shape is reserved: a method that does not wire a broker must not use it |
| `AddMediatRDomainEvents()` | on `MessagingBuilder` — wires the MicroKit.MediatR glue, four registrations: contributes the outbox `IDomainEventsSink` (`TryAddEnumerable`), decorates `IOutboxDispatcher` with the notification router, replaces `INotificationPublisher` with the cascade publisher, and `TryAdd`s an `IMessageSerializer` default the decorator requires. **Not a transport** — it moves nothing between processes (ADR-MEDIATR-015, ADR-MSG-016) |
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
