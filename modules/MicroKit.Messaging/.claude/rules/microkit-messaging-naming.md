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
| `IIntegrationEvent` | **bare marker** (ADR-MSG-018) — business payload only, `IEvent` base retained. Metadata lives on `IntegrationEventMessage`, assigned at staging |
| `IntegrationEventAttribute` | `[IntegrationEvent("name.v1")]` — mandatory wire contract name; the CLR type name cannot serve as one. Rejects null/empty/whitespace in its constructor, so one guard covers `Publishes<T>()` and `Consumes<T>()` alike — an empty name is a usable dictionary key and would bind, resolve and travel |
| `IIntegrationEventPublisher` | `PublishAsync<T>` — stages into the caller's open transaction, never commits, never delivers |
| `IIntegrationEventWriter` | `AddAsync` + `HasOpenTransaction` — staging port; stages only, never calls `SaveChangesAsync` |
| `IntegrationEventRegistry` | **bidirectional**. `ResolveContract(Type)` → the contract a type publishes under; `TryResolveLocalType(name)` / `ResolveLocalType(name)` → the local CLR type a wire name deserializes into. The reverse direction is what lets a consumer act on a payload without the producer's assembly — `Type.GetType(assemblyQualifiedName)` cannot cross a process. One local type per contract name per process; a second claimant is a boot failure. There is deliberately no `TryResolveContract`: a miss in the forward direction is a programming error, a miss in the reverse one is data off the wire |
| `IntegrationEventSubscription` | `sealed record (Type EventType, string ContractName)` — one consumed contract. Carries **no `Source`**, structurally: a consumer has none to declare |
| `IntegrationEventSubscriptionBuilder` | `Consumes<TEvent>()` — mirrors `Publishes<TEvent>()`, reads the same `[IntegrationEvent]` attribute, rejects its absence at registration |
| ~~`IMessagePublisher`~~ | **deleted** (ADR-MSG-018). The seam discarded the `OutboxMessage`, forcing the fan-out to re-read metadata off the event — the sole reason `IIntegrationEvent` had members. The in-process fan-out lives in `InProcessIntegrationDispatcher`; a real transport seam comes with the transport libraries |
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
| `IMessageTransport` | the transport seam — `SendAsync(MessageEnvelope, ct)`. **Returning means the destination acknowledged**; the processor marks the row `Published` on that return and `Published` is terminal, so an asynchronous hand-off makes the mark a lie. **No implementation ships in any MicroKit package** — a broker provider supplies one, and owes a conformance test proving the acknowledgement rule |
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
| `MessageEnvelope` | the **wire format** — `sealed record`, non-generic, carrying `MessageId` / `ContractName` / `Source` / opaque `Payload` / `TenantId` / correlation / causation / `OccurredOnUtc`. A **compatibility commitment**: adding a member is additive, removing or renaming one breaks every deployed consumer. Identifiers are bare `Guid`s, not the VO records — those serialize as `{"value":…}`, and a `JsonConverter` would make the wire shape depend on which serializer is registered. Replaced `MessageEnvelope<T>`, which was generic over a *deserialized* payload and carried no `ContractName` |
| `IntegrationEventMessage` | integration event EF Core entity — `sealed class` with `{ get; set; }`, its own table |
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
| `InProcess{Noun}` | `InProcessIntegrationDispatcher` — in-process default |
| `{Provider}{Noun}` | `RabbitMqMessagePublisher`, `AzureServiceBusPublisher` |
| `Ef{Noun}` | `EfOutboxStore`, `EfInboxStore` — EF Core implementations |
| `{Noun}Processor` | `OutboxProcessor`, `InboxProcessor` — topology-agnostic batch engines |
| `{Noun}Worker` | `OutboxWorker`, `InboxWorker`, `OutboxRetentionWorker`, `InboxRetentionWorker` — `BackgroundService` hosts. `internal sealed`; only `IServiceScopeFactory` is injected |
| `{Noun}Dispatcher` | `TransportOutboxDispatcher`, `InProcessIntegrationDispatcher`, `MediatROutboxDispatcher` — `IOutboxDispatcher` implementations, `internal sealed`. `TransportOutboxDispatcher` handles `MessageKind.Contract` and takes **no serializer and no registry**: the payload travels opaque, and it must keep `IMessageTransport` as a *constructor* dependency or a missing transport is misclassified as transient. NOT `MessageDispatcher`: that type was eliminated in favour of the `IOutboxDispatcher` seam and its re-introduction is blocked by an architecture test |
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
| `AddInProcessTransport()` | on `MessagingBuilder` — wires `IMessageSerializer` and the `InProcessIntegrationDispatcher`. No longer wires a publisher (ADR-MSG-018) |
| `AddTransportDispatcher()` | on `MessagingBuilder` — wires `TransportOutboxDispatcher` and **nothing else**: no serializer (it never deserializes) and no `IMessageTransport` (none ships). `TryAddScoped`, so it cannot displace a decorator. Deliberately **not** named `Add{Provider}Transport()` — that shape is reserved for methods that wire an actual broker, and a provider's own such method should call this one |
| `AddIntegrationEventContracts()` | on `IServiceCollection` — **once per module**; declares that module's published contracts and its `source`. Accumulates (`AddSingleton`, never `TryAdd`) so every module composes into one registry, which is what makes a cross-module name collision detectable |
| `AddIntegrationEventSubscriptions()` | on `IServiceCollection` — **once per module**; declares the contracts that module *understands* but does not publish. Takes **no `source`**: a consumer emitted nothing, and a nominal source would be false data in the one column that survives module extraction. Accumulates like the call above |
| `AddIntegrationEventPublishing()` | on `MessagingBuilder` — **once per application**; the registry, the publisher, a serializer default, and the startup validator |
| `AddIntegrationEventConsumption()` | on `MessagingBuilder` — **once per application**; the registry and the startup validator, and nothing else. Exists so a consumer-only service gets the same boot-time validation a publishing one gets. Safe alongside `AddIntegrationEventPublishing()` in either order — both share one `TryAdd`ed registry and one `TryAddEnumerable`d validator |
| `AddEfCoreIntegrationEvents<TContext>()` | on `MessagingBuilder` — the staging writer. Separate from the call above because Core has no EF Core dependency and must not acquire one |
| `Add{Provider}Transport()` | on `MessagingBuilder` — **broker providers ONLY** (e.g. `AddRabbitMqTransport()`). This shape is reserved: a method that does not wire a broker must not use it. **One known exception: `AddInProcessTransport()`**, which predates the reservation and wires no transport at all since ADR-MSG-018 (a serializer and the in-process dispatcher). Left as it is deliberately — renaming a public DI method is a break in itself, and a worse one than the inconsistency it would remove. Do not read it as precedent |
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
| `IMessageSerializer` | `Serialize(object)` → `string` (runtime type, never `typeof(T)`); `Deserialize(string payload, string eventType)` → `object?`. It serializes the **payload**, not an envelope. `MessageEnvelope` is a separate concern: the transport dispatcher copies the already-serialized payload into one and never round-trips it, so the send path resolves no type at all |
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
