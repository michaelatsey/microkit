# microkit-messaging-naming

The public surface is pinned name by name in
`tests/MicroKit.Messaging.ArchitectureTests/PublicSurfaceTests.cs`. A name that is not there is
internal, or does not exist. Adding a public type means editing that list on purpose.

---

## Type shapes

- `sealed record` — value objects, options, store keys, message types: `MessageId`,
  `CorrelationId`, `CausationId`, `MessageStoreKey`, `PublishOptions`, `LocalConsumer`,
  `MessagingOptions`, `OutboxProcessorOptions`, `InboxProcessorOptions`.
- `sealed class` with `required init` members — a type that crosses a seam and must gain members
  without breaking a call site: `MessageEnvelope`, `MessageContext`, `MessageSendContext`,
  `MessageReceiveContext`. No positional constructor (pinned by
  `MessageEnvelope_HasNoPositionalConstructor_SoAddingAHeaderNeverChangesOne`).
- `sealed record` with a private constructor and static factories when Core builds it and a
  provider reads it: `EnvelopeReceiveResult` (`Acknowledged`, `From(...)`).
- `readonly record struct` — small keys and outcomes: `InboxMessageKey`, `InboxRowState`,
  `OutboxOutcome`, `InboxOutcome`.
- `sealed class` with `{ get; set; }` — the EF rows `OutboxMessage` and `InboxMessage`: EF Core
  change tracking needs mutable properties.
- `sealed class` — services, handlers, processors, dispatchers, stores, exceptions.
- `I` prefix on every interface. No `Base`, `Helper`, `Utils` or `Manager` suffix: name by
  responsibility.

---

## Application surface (`MicroKit.Messaging`)

| Name | What it is |
|---|---|
| `IIntegrationEvent` | Marker, `: IEvent`. Business payload only; identity, tenant, correlation and time are assigned when the message is published |
| `MessageContractAttribute` | `[MessageContract("name", Version = n)]` — the wire contract. Wire type `WireType` = `"<name>.v<version>"`, `Version` defaults to 1. The constructor rejects a blank name and a name already ending in `.v<digits>`; a `Version` below 1 throws. Mandatory on every published or handled type: a CLR type name cannot cross a process |
| `IMessageBus` / `IEventPublisher` | `PublishAsync<T>(T message, PublishOptions? options = null, CancellationToken ct = default)` → `MessageId`. The one way to publish. One scoped instance serves both interfaces |
| `PublishOptions` | `TenantId`, `Headers`, `Store`. A reserved header in `Headers` throws |
| `MessageStoreKey` | `sealed record MessageStoreKey(string Value)`, never blank. Names a store |
| `IMessageHandler<T>` | `HandleAsync(T message, MessageContext context, CancellationToken ct = default)`. A handler is its subscription |
| `MessageContext` | `MessageId`, `ContractName`, `ContractVersion`, `Source`, `OccurredOnUtc`, `TenantId`, `CorrelationId`, `CausationId`, `Headers` |
| `IMessageSendMiddleware` / `MessageSendContext` | `InvokeAsync(context, proceed, ct)` around a publication. A middleware that does not call `proceed` makes `PublishAsync` throw |
| `IMessageReceiveMiddleware` / `MessageReceiveContext` | `InvokeAsync(context, proceed, ct)` around a handler |
| `IMessageScopeInitializer` | `InitializeAsync(MessageContext, IServiceProvider, ct)` — runs in each message scope before the handler and before an attempt's transaction begins |
| `MessageId` | `sealed record MessageId(Guid Value) : IComparable<MessageId>` |
| `PermanentMessageException`, `InboxDependencyUnavailableException` | Thrown by a handler: dead-letter now, or pause and redeliver without counting an attempt |
| `IntegrationEventConfigurationException`, `IntegrationEventPublishException` | Composition fault (start or publication), refused publication |
| Core: `MessagingBuilder`, `ServiceCollectionExtensions`, `MessagingDiagnostics`, `InMemoryBroker` | Composition, the inbox meter name (`InboxMeterName`), the in-memory broker a host may supply |
| `MicroKit.Messaging.Options` | `MessagingOptions` (section `MicroKit:Messaging`), `OutboxProcessorOptions`, `InboxProcessorOptions` |

## SPI (`MicroKit.Messaging.Spi`) — for store, transport and integration-package authors

| Name | What it is |
|---|---|
| `IMessageTransport` | `SendAsync(MessageEnvelope, ct)`. **Returning means the destination acknowledged**: the outbox marks the row `Published` on that return and `Published` is terminal. The only implementation shipped is Core's internal in-memory one (`OnlyCoreShipsAnIMessageTransport_AndItIsTheInternalInMemoryOne`) |
| `MessageEnvelope` | The wire form, CloudEvents-aligned: `Id` (string), `Source`, `Type` (the wire type), `Time`, `DataContentType`, `DataSchema?`, `Headers`, `Body` (`ReadOnlyMemory<byte>`). A compatibility commitment: renaming or removing a member breaks every deployed consumer |
| `MessageHeaders` | The reserved extensions `tenantid`, `correlationid`, `causationid`, `traceparent`; `IsReserved(name)` is case-insensitive |
| `IMessageSerializer` | `ContentType`; `Serialize(object message, Type type)` → `ReadOnlyMemory<byte>`; `Deserialize(ReadOnlyMemory<byte> body, Type type)` → `object?`. It serializes the message, never an envelope. The default is internal (`SystemTextJsonMessageSerializer`, `application/json`); only JSON is received, and the outbox refuses a non-JSON serializer |
| `IEnvelopeReceiver` | `ReceiveAsync(MessageEnvelope, ct)` → `EnvelopeReceiveResult`. Core implements it, a provider's consume loop calls it |
| `EnvelopeReceiveResult` / `ReceiveDisposition` | `Disposition`, `Reason`, `Error`. `Acknowledge`, `DeadLetter`, `Redeliver`, `PauseAndRedeliver`, `Unsettled`. `Unsettled` is never an acknowledgement |
| `IMessageStoreSet` | `GetStores()` — the stores the engine drains. Static, read at start |
| `MessageStoreBuilder` | `Key`, `Services`, `BindAssembly(Assembly)`, `AddStartupCheck(...)` |
| `MessageStoreRegistrationExtensions` | `AddMessageStore(this MessagingBuilder, MessageStoreKey, Action<MessageStoreBuilder>)` — how any store provider adds a store; the first call registers the engine and both outbox dispatchers |
| `IOutboxWriter` | `AddAsync`, `AddBatchAsync`. Enlists rows in the scope's unit of work; never saves |
| `IOutboxProcessorStore` | `ClaimBatchAsync(batchSize, lockDuration, ct)` → `OutboxClaim`, and the overload with `OutboxEndpoints excluded` (rows of an endpoint in back-off left out; the default ignores it); `ApplyOutcomesAsync(claimToken, outcomes, ct)` → rows written |
| `OutboxEndpoints` | `[Flags]` `None`, `Transport`, `Local` — the endpoints a claim leaves out |
| `IOutboxAdminStore` / `IOutboxRetentionStore` | `GetDeadLetteredAsync`, `RequeueAsync` / `DeleteProcessedAsync` |
| `IInboxGuardStore` | `FindAsync`, `ExecuteAsync` (the store's execution strategy, with commit verification), `BeginAsync`, `EnlistProcessedAsync` (new delivery), `MarkClaimedProcessedAsync` (retry), `CommitAsync`, `IsLeaseLost`, `TryInsertFailedAsync`. Scoped, keyed by the store key, one instance per attempt scope |
| `InboxRowState` | `(InboxMessageStatus Status, bool DeadLettered, Guid RowId)` — what `FindAsync` reads |
| `IInboxProcessorStore` | `ClaimBatchAsync(batchSize, leaseDuration, ct)` → `InboxClaim`; `ApplyOutcomesAsync` |
| `IInboxAdminStore` / `IInboxRetentionStore` | `GetDeadLetteredAsync`, `RequeueAsync(InboxMessageKey)` / `DeleteProcessedAsync` |
| `ILocalMessageSender` | `SendAsync(object message, MessageId messageId, DateTimeOffset occurredOnUtc, ct)` — a local message: never handed to a transport |
| `ILocalConsumerSource` / `LocalConsumer` | `GetConsumers()`; `LocalConsumer(MessageType, ConsumerType, HandlerType, Invoke)`. `ConsumerType` is the inbox key: keep it stable across deployments |
| `MessagingIntegrationExtensions` | `AddLocalConsumerSource(source)`, `AddStartupCheck(check)` on `MessagingBuilder` |
| `OutboxMessage`, `InboxMessage` | The rows. `OutboxMessage.Endpoint`: `null` = transport, `OutboxMessage.LocalEndpoint` (`"local"`) = local endpoint |
| `OutboxClaim`, `InboxClaim`, `OutboxOutcome`, `InboxOutcome`, `InboxMessageKey` | What the stores exchange. Outcome factories: `Published`/`Processed`, `Retry`, `DeadLetter`, `Released`, and the outbox's `Deferred` (an endpoint down: next attempt set, retry count and error kept) |
| `CorrelationId`, `CausationId` | `sealed record (Guid Value)` row columns |
| `OutboxMessageStatus`, `InboxMessageStatus`, `OutboxOutcomeKind`, `InboxOutcomeKind` | Row and outcome state, below |
| `OutboxPayloadException`, `OutboxTransportUnavailableException`, `OutboxConfigurationException`, `InboxPayloadException`, `InboxConfigurationException` | What a provider raises across the seam for the engine to classify |

## EF Core and MediatR packages

| Name | What it is |
|---|---|
| `MessagingBuilderExtensions.UseEntityFrameworkOutbox<TContext>(Action<EntityFrameworkOutboxBuilder>? configure = null)` | The EF store: the outbox and the inbox of `TContext`. A second call for the same context reaches the same store |
| `EntityFrameworkOutboxBuilder` | `Key`, `ForAssemblyContaining<TMarker>()` |
| `EntityFrameworkOutbox.KeyOf<TContext>()` | The store key, `"ef:" + typeof(TContext).FullName` |
| `ModelBuilderExtensions.ApplyMessagingConfiguration()`, `OutboxMessageConfiguration`, `InboxMessageConfiguration` | The only model hook |
| `MessagingMediatRExtensions.UseMediatRDomainEvents()` | Namespace `MicroKit.Messaging.MediatR.DependencyInjection`. Idempotent |

---

## Row state

| Value | Meaning |
|---|---|
| `OutboxMessageStatus.Pending` | Not yet dispatched, or back after a retry or a release. Claimable when not dead-lettered and `NextRetryAtUtc` is null or past |
| `OutboxMessageStatus.Processing` | Claimed: `LockedUntilUtc` and `ClaimToken` set. Reclaimable once the lease expires |
| `OutboxMessageStatus.Published` | The transport acknowledged, or the local endpoint delivered. Terminal |
| `OutboxMessageStatus.Failed` | Always terminal, `DeadLettered = true` in the same write. There is no transient outbox failure state: a failed attempt goes back to `Pending` |
| `InboxMessageStatus.Received` | Written by no path since #188; still claimed by the retry loop so a row from an earlier version is handled |
| `InboxMessageStatus.Processing` | Claimed by a store's retry loop: lease until `LockedUntilUtc`, `ClaimToken` names the owner |
| `InboxMessageStatus.Processed` | Written in the transaction that committed the handler's work. Terminal |
| `InboxMessageStatus.Failed` | The last attempt failed. `DeadLettered = false`: retried once `NextRetryAtUtc` passes. `DeadLettered = true`: terminal. A released retry also returns to `Failed`, with nothing counted |

One inbox row per (`MessageId`, `ConsumerType`); the unique index
`UX_InboxMessages_MessageId_ConsumerType` is the guard. `ConsumerType` is the handler type's
`AssemblyQualifiedName` (a bus handler) or `LocalConsumer.ConsumerType`.

---

## Internal implementation names

| Pattern | Types |
|---|---|
| `Ef{Noun}Store<TContext>` | `EfOutboxStore<TContext>`, `EfInboxStore<TContext>` — `internal sealed`, scoped, every store interface resolving to the one instance of the scope |
| `{Noun}Processor` | `OutboxProcessor`, `InboxProcessor` — one batch; behind internal `IOutboxProcessor` / `IInboxProcessor`, keyed by store |
| `{Noun}Loop` | `OutboxLoop`, `InboxLoop`, `OutboxRetentionLoop`, `InboxRetentionLoop` — plain classes, one of each per store, each with its own back-off |
| `MessageStoreEngine` | The `BackgroundService` that runs the four loops of every store. Registered by the first `AddMessageStore`, once |
| `{Noun}Worker` | `InMemoryTransportWorker` (the in-memory consume loop), `LocalQueueWorker` (the in-memory local queue). `BackgroundService` / hosted services, `IServiceScopeFactory` only in the constructor |
| `{Noun}Dispatcher` | `TransportOutboxDispatcher`, `LocalOutboxDispatcher` — internal `IOutboxDispatcher`, keyed `OutboxDispatchers.Transport` / `OutboxDispatchers.Local`, both registered with the first store, chosen by `OutboxDispatchers.KeyOf(row)` from `OutboxMessage.Endpoint`, never from the payload's type. `TransportOutboxDispatcher` takes no serializer: the payload travels opaque. `MessageDispatcher` and `InProcessIntegrationDispatcher` must not come back (architecture tests) |
| `MessageReceivePipeline` | The one receive path for transport deliveries, local deliveries and retries; it holds the inbox guard |
| `EnvelopeReceiver` | `IEnvelopeReceiver`: maps a delivery verdict to a `ReceiveDisposition` |
| `{Noun}Registry` | `IntegrationEventRegistry` (wire type ↔ local type), `MessageHandlerRegistry`, `LocalConsumerRegistry` |
| MediatR glue | `DomainEventNotificationSink` (an `IDomainEventsSink` sending through `ILocalMessageSender`), `DomainEventsDispatchMiddleware` (dispatches cascade events after the handler), `NotificationHandlerSource` (each `INotificationHandler` a `LocalConsumer`) |
| Test doubles (tests only) | `Recording{Noun}`, `Scripted{Noun}`, `Capturing{Noun}`, `InMemoryInbox{Noun}` — there is no `MicroKit.Messaging.Testing` package |
| Broker provider | `{Provider}Transport`, registered by `Add{Provider}Transport()`. The `Add{Provider}Transport()` shape is reserved for methods that wire a real broker |

---

## Message types

```csharp
// ✅ A past-tense domain fact, with its contract. Wire type: shop.orders.order-placed.v1
[MessageContract("shop.orders.order-placed")]
public sealed record OrderPlaced(Guid OrderId) : IIntegrationEvent;

// ✅ A breaking change is a new version: a second contract, a second type.
[MessageContract("shop.orders.order-placed", Version = 2)]
public sealed record OrderPlacedV2(Guid OrderId, Guid CustomerId) : IIntegrationEvent;

// ❌ Version in the name — the attribute throws: set Version instead.
[MessageContract("shop.orders.order-placed.v1")]

// ❌ Generic noun, "On" prefix, "Notification" suffix (a MediatR notification is a domain-event
//    notification, not an integration event).
public sealed record OrderMessage;
public sealed record OnOrderPlaced;
```

A consumer declares the producer's name on its **own** type. Two types claiming one wire type in
one process fail at start.

---

## Composition methods

| Method | On | Notes |
|---|---|---|
| `AddMicroKitMessaging(Action<MessagingBuilder>)` | `IServiceCollection` | The one entry point; may be called once per module, the calls add to one composition. Registers the bus, the receive pipeline, `IEnvelopeReceiver`, the serializer default (`TryAdd`, so a host's earlier registration wins), the local sender. Its only hosted service is the composition validator |
| `ServiceName` | `MessagingBuilder` | The `Source` of every publication and the endpoint consumed from. Blank throws; `null` leaves it unset |
| `Publishes<T>()` | `MessagingBuilder` | Declares a publication; required to publish `T` |
| `AddHandler<THandler>()` / `AddHandlersFromAssemblyContaining<TMarker>()` | `MessagingBuilder` | One subscription per closed `IMessageHandler<T>`; the scan is `[RequiresUnreferencedCode]` |
| `Configure`, `ConfigureJson`, `AddSendMiddleware<T>`, `AddReceiveMiddleware<T>`, `AddScopeInitializer<T>` | `MessagingBuilder` | |
| `UseInMemoryTransport(InMemoryBroker? broker = null)` | `MessagingBuilder` | The in-memory transport, its consume loop, and the transport dispatcher. Refuses when another `IMessageTransport` is registered |
| `AddMessageStore(key, configure)` | `MessagingBuilder` (Spi) | Store providers. The first call registers the engine and both outbox dispatchers |
| `AddLocalConsumerSource(source)`, `AddStartupCheck(check)` | `MessagingBuilder` (Spi) | Integration packages; the first source brings `LocalQueueWorker` |
| `UseEntityFrameworkOutbox<TContext>()` | `MessagingBuilder` | EF package |
| `UseMediatRDomainEvents()` | `MessagingBuilder` | MediatR package. Not a transport |

---

## Test names

PascalCase segments joined by `_`, either form:

```
{Member}_{Scenario}_{ExpectedResult}      for one member of one type
  ClaimBatchAsync_WhenManyProcessorsRaceOnOneRow_ExactlyOneWins
  ApplyOutcomesAsync_WithStaleToken_WritesNothing
  ReceiveAsync_WhenOneHandlersRowExists_SkipsItAndStillRunsTheOthers
  DispatchAsync_DoesNotDeserializeThePayload

{Situation}_{Outcome}                     for a behaviour across components
  TwoHandlers_OneFailing_TheOtherRunsOnce_AndTheFailingOneIsRetriedToProcessed
  AStoreWhoseDbContextIsNotRegistered_FailsAtStart

❌ TestOutboxProcessor   ❌ ShouldPublishMessage   ❌ a name citing a member that no longer exists
```

---

## Files

| Kind | Convention | Example |
|---|---|---|
| Interface | `I{Name}.cs` | `IInboxGuardStore.cs` |
| Implementation | `{Name}.cs` | `EfOutboxStore.cs`, `OutboxLoop.cs` |
| Options | `{Name}Options.cs` | `OutboxProcessorOptions.cs` |
| Extensions | `{Name}Extensions.cs` | `MessageStoreRegistrationExtensions.cs` |
| Logging | `{Area}Logs.cs`, `[LoggerMessage]` | `InboxGuardLogs.cs` |
| EF configuration | `{Entity}Configuration.cs` | `InboxMessageConfiguration.cs` |
| Tests | `{Subject}Tests.cs` | `OutboxProcessorTests.cs`, `HandlerIsolationTests.cs` |

---

## Removed in #188 — do not reintroduce or cite

`IIntegrationEventPublisher`, `IIntegrationEventWriter`, `IntegrationEventWriteResult`,
`IInboxWriter`, `IInboxSettlementStore`, `InboxWriteResult`, `MessageKind`,
`OutboxDispatcherKeys`, `MediatROutboxDispatcher`, `DomainEventsCascadeNotificationPublisher`,
`OutboxMessage.OriginMessageId`, `OutboxWorker`, `InboxWorker`, the coordinators,
`AddEfCoreOutbox`, `AddEfCoreIntegrationEvents`, `AddMediatRDomainEvents`,
`AddOutboxInboxProcessing` (`OutboxInboxProcessingExtensions`). Earlier: `IntegrationEventAttribute`, `IMessagePublisher`,
`AddInProcessTransport`.
