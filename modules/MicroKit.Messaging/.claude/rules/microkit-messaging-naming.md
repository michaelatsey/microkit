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
| ~~`IMessagePublisher`~~ | **deleted** (ADR-MSG-018). The seam discarded the `OutboxMessage`, forcing the fan-out to re-read metadata off the event — the sole reason `IIntegrationEvent` had members. The in-process fan-out that replaced it is itself **deleted** (ADR-MSG-019): the transport seam arrived, and a `Contract` row now leaves through `IMessageTransport` while the receiving side writes its own inbox rows |
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
| `{Provider}{Noun}` | `RabbitMqMessagePublisher`, `AzureServiceBusPublisher` |
| `Ef{Noun}` | `EfOutboxStore`, `EfInboxStore` — EF Core implementations |
| `{Noun}Processor` | `OutboxProcessor`, `InboxProcessor` — topology-agnostic batch engines |
| `{Noun}Worker` | `OutboxWorker`, `InboxWorker`, `OutboxRetentionWorker`, `InboxRetentionWorker` — `BackgroundService` hosts. `internal sealed`; only `IServiceScopeFactory` is injected |
| `{Noun}Dispatcher` | `TransportOutboxDispatcher`, `MediatROutboxDispatcher` — `IOutboxDispatcher` implementations, `internal sealed`, **routing on `MessageKind` and never on the payload's CLR type**. `TransportOutboxDispatcher` handles `MessageKind.Contract` and takes **no serializer and no registry**: the payload travels opaque, and it must keep `IMessageTransport` as a *constructor* dependency or a missing transport is misclassified as transient. `MediatROutboxDispatcher` handles `MessageKind.Notification` and delegates the rest to an **optional** inner resolved from `OutboxDispatcherKeys.Standard`. NOT `MessageDispatcher`, and no longer `InProcessIntegrationDispatcher`: both were eliminated and both re-introductions are blocked by architecture tests |
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
| `AddMicroKitMessaging()` | on `IServiceCollection` — main registration entry point. **Owns the `IMessageSerializer` default** (ADR-MSG-019): it registers `InboxProcessor` and `OutboxMessageFactory` unconditionally and both require one, so no optional builder method may claim that job. Also contributes `InboxIngestionValidator` |
| `AddEfCoreOutbox()` | on `MessagingBuilder` — wires `EfOutboxStore` and `EfInboxStore`. Each is registered **once as scoped by concrete type**, with every interface resolving to that instance through a factory lambda, so one scope holds one store over one `DbContext`. That is also what makes `IInboxSettlementStore` transactional: resolved from the per-message execution scope it necessarily shares its `TContext` with the handler resolved from the same scope. Registering either store as anything other than scoped breaks the guarantee silently. The name is now a misnomer — it wires the inbox too |
| ~~`AddInProcessTransport()`~~ | **deleted** (ADR-MSG-019). Its dispatcher is gone and its `IMessageSerializer` default moved to `AddMicroKitMessaging()`, which owns the two types that require one (`InboxProcessor`, `OutboxMessageFactory`). Migration: `AddTransportDispatcher()`, or nothing at all for a notification-only host |
| `AddTransportDispatcher()` | on `MessagingBuilder` — wires `TransportOutboxDispatcher` and **nothing else**: no serializer (it never deserializes) and no `IMessageTransport` (none ships). **Two registrations**: the dispatcher `TryAddKeyedScoped` under `OutboxDispatcherKeys.Standard`, plus an unkeyed `TryAddScoped` forwarder for the seam `OutboxProcessor` resolves. The split is what makes composition order-independent — only Core writes the keyed slot, and a decorator takes the unkeyed one outright (ADR-MSG-019). ⚠ Calling it without registering an `IMessageTransport` stops **every** kind of row, notifications included. Deliberately **not** named `Add{Provider}Transport()` — that shape is reserved for methods that wire an actual broker, and a provider's own such method should call this one |
| `AddIntegrationEventContracts()` | on `IServiceCollection` — **once per module**; declares that module's published contracts and its `source`. Accumulates (`AddSingleton`, never `TryAdd`) so every module composes into one registry, which is what makes a cross-module name collision detectable |
| `AddIntegrationEventSubscriptions()` | on `IServiceCollection` — **once per module**; declares the contracts that module *understands* but does not publish. Takes **no `source`**: a consumer emitted nothing, and a nominal source would be false data in the one column that survives module extraction. Accumulates like the call above |
| `AddIntegrationEventPublishing()` | on `MessagingBuilder` — **once per application**; the registry, the publisher and the startup validator. It registers **no** serializer default: `AddMicroKitMessaging()` owns that, and this is an extension on the builder that method returns, so a `TryAdd` here would be unreachable code reading like a safeguard |
| `AddIntegrationEventConsumption()` | on `MessagingBuilder` — **once per application**; the registry and the startup validator, and nothing else. Exists so a consumer-only service gets the same boot-time validation a publishing one gets. Safe alongside `AddIntegrationEventPublishing()` in either order — both share one `TryAdd`ed registry and one `TryAddEnumerable`d validator |
| `AddEfCoreIntegrationEvents<TContext>()` | on `MessagingBuilder` — the staging writer. Separate from the call above because Core has no EF Core dependency and must not acquire one |
| `Add{Provider}Transport()` | on `MessagingBuilder` — **broker providers ONLY** (e.g. `AddRabbitMqTransport()`). This shape is reserved: a method that does not wire a broker must not use it. The one standing exception, `AddInProcessTransport()`, was deleted by ADR-MSG-019, so the reservation is now absolute |
| `AddMediatRDomainEvents()` | on `MessagingBuilder` — wires the MicroKit.MediatR glue, **three** registrations: contributes the outbox `IDomainEventsSink` (`TryAddEnumerable`), takes the unkeyed `IOutboxDispatcher` seam with the notification router, and replaces `INotificationPublisher` with the cascade publisher. It requires no transport to have been registered first and no longer supplies a serializer (ADR-MSG-019). **Not a transport** — it moves nothing between processes (ADR-MEDIATR-015, ADR-MSG-016) |
| `AddMessageHandler<THandler, TEvent>()` | on `MessagingBuilder` — registers a handler. ⚠ **Fails at startup in this release**: nothing produces inbox rows until the receiving seam ships, so `InboxIngestionValidator` throws rather than let a host believe it consumes events (ADR-MSG-019) |

---

## Test Method Naming

```
{Method}_{Scenario}_{ExpectedResult}

Examples — every one names an API that still exists:
  PublishAsync_WhenEventValid_StoresInOutbox
  PublishAsync_WithNoOpenTransaction_ThrowsAndStagesNothing
  ClaimBatchAsync_WhenTwoProcessorsRaceOnOneRow_ExactlyOneWins
  ApplyOutcomesAsync_WithStaleToken_WritesNothing (the lost update)
  ExistsAsync_WhenMessageAlreadyProcessed_ReturnsTrue (dedup gate)
  DispatchAsync_WhenKindIsContract_NeverTouchesTheSerializer

⚠ Do NOT copy `GetPendingAsync` / `MarkPublishedAsync` / `MarkFailedAsync` from older examples —
the per-message lease API was removed by the outbox and inbox claim rewrites (see the store rows
above). A test named after a deleted method is a test nobody can write.
```

---

## Files

| Type | Convention | Example |
|------|-----------|---------|
| Interface | `I{Name}.cs` | `IOutboxProcessorStore.cs`, `IMessageTransport.cs` |
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
