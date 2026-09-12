# MicroKit.Messaging — Module Brain

## 🎯 Purpose

MicroKit.Messaging is the **reliable message-passing layer** of the MicroKit ecosystem. It provides
a transactional outbox, an inbox with idempotent dedup, and an in-process transport for local
delivery — all without coupling to any specific message broker.

> **Core principle:** the domain publishes integration events; this module guarantees at-least-once
> delivery through the outbox pattern. Broker coupling is optional and lives in separate provider
> packages (`RabbitMQ`, `AzureServiceBus`, `Kafka`).

```
Your domain / command handlers
        │ publishes IIntegrationEvent
        ▼
MicroKit.Messaging            ← transactional outbox, inbox dedup, transport seam
        │
        ├── OutboxProcessor (IHostedService)   ← polls and dispatches pending messages
        ├── InboxProcessor  (IHostedService)   ← drains inbound rows written by IEnvelopeReceiver
        └── IOutboxDispatcher                  ← routes on MessageKind, never on a CLR type
                │
                ├── TransportOutboxDispatcher  ← Kind=Contract → IMessageTransport (Core)
                └── MediatROutboxDispatcher    ← Kind=Notification → IPublisher.Publish,
                                                 delegates the rest inward (.MediatR)
                        │
                        └── IMessageTransport  ← NO IMPLEMENTATION SHIPS
                                ├── RabbitMQ           ← v2 provider (planned)
                                ├── AzureServiceBus    ← v2 provider (planned)
                                └── Kafka              ← v2 provider (planned)
```

---

## 🗺️ Navigation

Always load the relevant file before working on a specific concern:

| Task | Load first | Agent |
|------|-----------|-------|
| **Implementing anything new** | `.claude/CLAUDE.md` + relevant rule | `microkit-messaging-implementer` — plan before code |
| Architecture / contract decision | `.claude/rules/microkit-messaging-architecture.md` + `.claude-context/context/microkit-messaging-architectural-decisions.md` | `microkit-messaging-architect` |
| Outbox/Inbox concern | `.claude/rules/microkit-messaging-outbox-inbox.md` | `microkit-messaging-architect` |
| AsyncLocal / context propagation | `.claude/rules/microkit-messaging-architecture.md` | `microkit-messaging-distributed-context-specialist` — mandatory |
| Public API change | `.claude/rules/microkit-messaging-naming.md` + `.claude/rules/microkit-messaging-architecture.md` | `microkit-messaging-api-reviewer` — required before merge |
| Dependency / `.csproj` change | `.claude/rules/microkit-messaging-dependencies.md` | `microkit-messaging-dependency-guardian` — auto on `.csproj` edit |
| New broker provider | `.claude/commands/microkit-messaging-new-provider.md` | `microkit-messaging-implementer` |
| Release | `.claude/commands/microkit-messaging-release.md` | `microkit-messaging-release-manager` |

---

## 🏛️ Module Structure

```
MicroKit.Messaging/
├── src/
│   ├── MicroKit.Messaging.Abstractions/        ← IIntegrationEvent, IOutboxDispatcher,
│   │                                              OutboxDispatcherKeys,
│   │                                              IMessageHandler<T>, IOutboxWriter,
│   │                                              IOutboxProcessorStore, IInboxWriter,
│   │                                              IInboxProcessorStore, IInboxSettlementStore,
│   │                                              OutboxMessage (sealed class), InboxMessage (sealed class),
│   │                                              IMessageTransport, MessageEnvelope (wire form)
│   ├── MicroKit.Messaging/                     ← OutboxProcessor, InboxProcessor,
│   │                                              TransportOutboxDispatcher (Kind=Contract →
│   │                                              IMessageTransport) — it READS
│   │                                              OutboxDispatcherKeys, which lives in
│   │                                              Abstractions above, not here,
│   │                                              EnvelopeReceiver (the RECEIVING seam — the only
│   │                                              writer of InboxMessage rows in the module),
│   │                                              IntegrationEventPublisher, DI, background workers
│   ├── MicroKit.Messaging.EntityFrameworkCore/ ← EfOutboxStore, EfInboxStore, EF configurations,
│   │                                              migrations helper
│   └── MicroKit.Messaging.Testing/             ← FakeMessagePublisher, InMemoryOutboxStore,
│                                                  InMemoryInboxStore, assertion helpers
│
│   ── v2 providers (IsPackable=false until implemented) ──────────────────
│   ├── MicroKit.Messaging.RabbitMQ/            ← RabbitMQ v7 broker adapter [Phase 2]
│   ├── MicroKit.Messaging.AzureServiceBus/     ← Azure Service Bus adapter  [Phase 2]
│   ├── MicroKit.Messaging.Kafka/               ← Confluent Kafka adapter    [Phase 2]
│   ├── MicroKit.Messaging.OpenTelemetry/       ← OTel tracing for messages  [Phase 2]
│   └── MicroKit.Messaging.Serialization/       ← JSON/Avro serialization    [Phase 2]
│
├── tests/
│   ├── MicroKit.Messaging.UnitTests/
│   ├── MicroKit.Messaging.IntegrationTests/
│   ├── MicroKit.Messaging.ArchitectureTests/
│   └── MicroKit.Messaging.PerformanceTests/
│
├── benchmarks/
├── samples/
└── MicroKit.Messaging.slnx
```

---

## 📦 Dependency Graph

```
MicroKit.Messaging.Abstractions
    ← MicroKit.Result
    ← MicroKit.Domain            (ADR-MSG-010: IIntegrationEvent : IEvent — canonical event taxonomy
                                  root from MicroKit.Domain.Events; does NOT extend IDomainEvent)

MicroKit.Messaging (Core)
    ← MicroKit.Messaging.Abstractions
    ← Microsoft.Extensions.DependencyInjection.Abstractions
    ← Microsoft.Extensions.Hosting.Abstractions            (IHostedService)
    ← Microsoft.Extensions.Logging.Abstractions

MicroKit.Messaging.EntityFrameworkCore
    ← MicroKit.Messaging (Core)
    ← MicroKit.Persistence.EntityFrameworkCore
    ← Microsoft.EntityFrameworkCore

MicroKit.Messaging.Testing
    ← MicroKit.Messaging.Abstractions

── v2 providers (planned) ──────────────────────────────────────────────────────
MicroKit.Messaging.RabbitMQ            ← MicroKit.Messaging + RabbitMQ.Client v7
MicroKit.Messaging.AzureServiceBus     ← MicroKit.Messaging + Azure.Messaging.ServiceBus
MicroKit.Messaging.Kafka               ← MicroKit.Messaging + Confluent.Kafka
MicroKit.Messaging.OpenTelemetry       ← MicroKit.Messaging + OpenTelemetry.Api
MicroKit.Messaging.Serialization       ← MicroKit.Messaging.Abstractions + System.Text.Json
```

**MicroKit.Messaging is a Level 3 module.** It may depend on:
- Level 0: `MicroKit.Result`, `MicroKit.Domain`
- Level 2: `MicroKit.Persistence.EntityFrameworkCore` (in `.EntityFrameworkCore` package only)

**Forbidden:** any dependency on `MicroKit.Auth`, `MicroKit.Multitenancy`, `MicroKit.MediatR`,
`MicroKit.Http`, or `MediatR.Contracts` — **except the `MicroKit.Messaging.MediatR` glue package**,
which may reference `MicroKit.MediatR` / `MediatR` / `MediatR.Contracts` (ADR-MSG-009 carve-out).

---

## 🔑 Key Contracts (quick reference)

### Event contracts
```csharp
IIntegrationEvent                  // BARE MARKER (ADR-MSG-018) — business payload only. It once
                                   //   declared MessageId/TenantId/CorrelationId/CausationId/
                                   //   OccurredOnUtc; all message metadata now lives on the row,
                                   //   assigned at staging from IExecutionContext
IntegrationEventAttribute          // [IntegrationEvent("name.v1")] — mandatory wire contract name
IIntegrationEventPublisher         // PublishAsync<T>(evt, occurredOnUtc, ct) → ValueTask<MessageId>
                                   //   writes a Contract row into the CALLER's open transaction;
                                   //   never commits. A replayed publication returns the EXISTING
                                   //   row's id and the handler is told nothing
IIntegrationEventWriter            // AddAsync(OutboxMessage) -> IntegrationEventWriteResult +
                                   //   HasOpenTransaction. Writes a Contract row into the OUTBOX;
                                   //   it flushes (the replay key must be attempted) but never commits
IntegrationEventRegistry           // BIDIRECTIONAL — ResolveContract(Type) → contract; ResolveLocalType(name)
                                   //   → local CLR type. The reverse direction is the precondition
                                   //   for any transport: a consumer lacks the producer's assembly,
                                   //   so Type.GetType(AQN) cannot resolve across a process
IntegrationEventSubscription       // sealed record (Type, ContractName) — no Source, deliberately
IntegrationEventWriteResult        // sealed record — Staged / AlreadyPublishedAs(existingId).
                                   //   A replayed publication is REPORTED, never thrown
MessageId                          // sealed record — strongly-typed message identifier
CorrelationId                      // sealed record — correlation chain identifier
CausationId                        // sealed record — causal parent identifier (nullable on root events)
```

### Publishing
```csharp
// IMessagePublisher is DELETED (ADR-MSG-018). It handed a dispatcher's payload on as a bare
// event, so the fan-out behind it had to reconstruct message metadata by reading it off the
// event — the sole reason IIntegrationEvent carried those members. The in-process fan-out that
// replaced it is DELETED TOO (ADR-MSG-019): it wrote inbox rows on the PRODUCING side, which is
// the confusion the contract-name indirection exists to remove.
// The real transport seam has now arrived: IMessageTransport + MessageEnvelope, fed by
// TransportOutboxDispatcher. NO IMessageTransport IMPLEMENTATION SHIPS — a broker provider
// supplies one, and owes a conformance test that SendAsync does not return before the broker
// acknowledges. With none registered, a Contract row releases the batch, consumes no retry
// budget and stops the worker (OutboxConfigurationException) rather than failing silently.
// IMessageDispatcher is internal to Core — not a public Abstractions contract
```

### Handling
```csharp
IMessageHandler<T>                 // HandleAsync(T evt, CancellationToken ct) → ValueTask
```

### Outbox / Inbox stores (in Abstractions)
```csharp
IOutboxWriter                      // AddAsync + AddBatchAsync — write-only, used by domain handlers
                                   //   in transaction; no processor operations (ADR-MSG-011)
IOutboxProcessorStore              // ClaimBatchAsync (atomic batch claim + ownership token),
                                   //   ApplyOutcomesAsync (one settlement per batch) — processor only
IOutboxAdminStore                  // GetDeadLetteredAsync, RequeueAsync — operator tooling only
IOutboxRetentionStore              // DeleteProcessedAsync — the retention worker only
IInboxWriter                       // ExistsAsync + AddAsync — ingestion only. AddAsync returns
                                   //   InboxWriteResult (Added / AlreadyPresent); a redelivery is
                                   //   reported, never thrown (ADR-MSG-017)
IInboxProcessorStore               // ClaimBatchAsync (atomic batch claim + ownership token),
                                   //   ApplyOutcomesAsync — the drain processor only
IInboxSettlementStore              // StageProcessedAsync + IsMarkUncommitted + IsLeaseLost —
                                   //   resolved from the PER-MESSAGE scope so the processed mark
                                   //   commits in the handler's own transaction
IInboxAdminStore                   // GetDeadLetteredAsync, RequeueAsync — operator tooling only
IInboxRetentionStore               // DeleteProcessedAsync — the inbox retention worker only
```

### Outbox / Inbox messages
```csharp
OutboxMessage                      // sealed class — EF Core entity; Id, TenantId, EventType, Payload,
                                   //   Status, RetryCount, LockedUntilUtc, NextRetryAtUtc, DeadLettered, ...
InboxMessage                       // sealed class — EF Core entity; MessageId, ConsumerType, Status, ...
IEnvelopeReceiver                  // the RECEIVING seam — ReceiveAsync(MessageEnvelope, ct) →
                                   //   EnvelopeReceiveResult. Mirror of IMessageTransport, opposite
                                   //   direction: Core IMPLEMENTS this and a provider's consume loop
                                   //   calls it. Returning means the rows are durably committed and
                                   //   the broker may be acknowledged. Fans out contract name →
                                   //   local type → one InboxMessage per registered consumer
EnvelopeReceiveResult              // sealed record — RowsAdded / Duplicates / ConsumersMatched.
                                   //   ConsumersMatched == 0 is legal and never silent
MessageEnvelope                    // sealed record — the WIRE FORM. MessageId/ContractName/Source/
                                   //   opaque Payload/TenantId/Correlation/Causation/OccurredOnUtc.
                                   //   A COMPATIBILITY COMMITMENT: adding a member is additive,
                                   //   removing or renaming one breaks every deployed consumer.
                                   //   Identifiers are bare Guids, not the VO records
```

---

## 📐 Non-Negotiable Rules

1. **`IIntegrationEvent` (not `INotification`)** — no MediatR dependency anywhere in Messaging
2. **`IOutboxWriter` and `IOutboxProcessorStore` live in `Messaging.Abstractions`** — never in `Persistence.Abstractions`
3. **Tenant-aware, but `TenantId` is NULLABLE** — `string?` on `OutboxMessage` and
   `InboxMessage`, with no `IsRequired()` and no global query filter. Messaging must run without
   Tenancy (ADR-EXEC-001), and a single-tenant deployment legitimately has null on every row
   (ADR-MSG-008 §5). `TenantId` travels **on the row** and is read from there by the processors,
   never from `IHttpContextAccessor` — and never used as a filter on the claim, which is
   cross-tenant by design (ADR-MSG-002)
4. **Outbox states** — `Pending → Processing → Published` or `Failed+DeadLettered=true`; **`Failed` always means terminal** (DeadLettered=true)
5. **Inbox dedup key** = `(MessageId + ConsumerType)` — a **unique index** is the real guard, and
   the sole authority. The primary key is the `RowId` surrogate (ADR-MSG-017): a compound-key
   claim filters an `UPDATE` with two `Contains` and selects the CROSS PRODUCT of both lists,
   which claims rows nobody chose and breaks the `batchSize` bound. A redelivery is reported
   through `InboxWriteResult.AlreadyPresent`, never thrown
6. **No silent success** — a path that cannot deliver must throw, never return as if it had.
   A dispatcher throws `OutboxPayloadException` on a row it can never serve and
   `OutboxConfigurationException` on one this composition merely cannot serve yet;
   `IIntegrationEventPublisher` throws `IntegrationEventPublishException` rather than let its
   writer's flush be committed by the provider's implicit transaction — an event announced for
   a fact that may still roll back; and `EnvelopeReceiver` logs at `Warning` and counts
   `microkit.inbox.envelopes.unconsumed` when a contract resolves to a local type nothing
   consumes — zero rows is correct there, but a *silent* zero is indistinguishable from a
   healthy delivery, which is the shape of every silent-success defect in this module
7. **Background processors never use `IHttpContextAccessor`** — `TenantId` read from `OutboxMessage`/`InboxMessage` only
8. **`sealed class`** for EF Core entities (`OutboxMessage`, `InboxMessage`) | **`sealed record`** for VOs (`MessageId`, `CorrelationId`, `CausationId`, options) | **`sealed class`** for processors/handlers/publishers
9. **`ValueTask<T>`** for all async methods | **`ConfigureAwait(false)`** throughout lib code
   **The ADR-MSG-014 exception is gone (ADR-MSG-015, then ADR-MSG-017).** All four seams now
   return a batch result: `IOutboxCoordinator.ExecuteAsync` / `IOutboxProcessor.ProcessBatchAsync`
   return `ValueTask<OutboxBatchResult>`, and `IInboxCoordinator.ExecuteAsync` /
   `IInboxProcessor.ProcessBatchAsync` return `ValueTask<InboxBatchResult>`. `BackgroundService`
   overrides still return `Task`, which is the framework's signature, not ours.
10. **`CancellationToken ct = default`** always last parameter
11. **`Console.WriteLine` forbidden** → `ILogger<T>`
12. **No inline `Version=`** on `PackageReference` — CPM via root `Directory.Packages.props`
13. **XML docs on all public members** in `src/` projects
14. **`MediatR.Contracts` forbidden everywhere** — in all packages, production and test, **except the `MicroKit.Messaging.MediatR` glue** (ADR-MSG-009 carve-out: the glue bridges domain-event notifications onto the outbox via `IPublisher.Publish`)
15. **`FluentAssertions` forbidden** — use Shouldly (MIT)
16. **Scope-per-message mandatory** in `OutboxProcessor` and `InboxProcessor` — never share one
    scope across a batch. On the inbox this is load-bearing twice over: the per-message scope
    is also what makes `IInboxSettlementStore` resolve against the same `DbContext` the
    handler writes through, so the processed mark commits in the handler's own transaction
17. **The claim must be atomic** — `ClaimBatchAsync` stamps candidates with a single `UPDATE WHERE`
    via `ExecuteUpdateAsync`, replaying the eligibility predicate inside the UPDATE; SELECT+mutate+SaveChanges
    is forbidden. Every terminal write additionally filters on `ClaimToken`, so a processor whose lease
    expired mid-dispatch matches zero rows instead of overwriting the processor that took its messages over.

18. **The inbox claim is the same shape, with one addition that is not optional.** `ClaimBatchAsync`
    stamps candidates selected by their **single-column `RowId`**, replaying the eligibility
    predicate inside the `UPDATE`; every deferred write filters on `ClaimToken`. On top of that,
    `ClaimToken` is mapped as an **EF concurrency token**, which is what makes the staged
    `StageProcessedAsync` mark safe: without it the `UPDATE` that `SaveChanges` emits carries the
    primary key alone, ownership is checked at read time only, and the lost update the token exists
    to prevent comes straight back. Removing that mapping breaks no test that does not exercise
    concurrency (ADR-MSG-017).

---

## 🤖 Available Agents

| Agent | Model | Trigger |
|-------|-------|---------|
| `microkit-messaging-implementer` | Opus | **First agent to invoke** before writing any code — produces plan, waits for approval |
| `microkit-messaging-architect` | Opus | Outbox/inbox design, contract decisions, module boundary changes |
| `microkit-messaging-api-reviewer` | Opus | Public API surface in Abstractions or Core — required before merge |
| `microkit-messaging-dependency-guardian` | Haiku | Any `.csproj` change — fast PASS/BLOCK |
| `microkit-messaging-distributed-context-specialist` | Opus | AsyncLocal propagation in outbox/inbox processors, background worker scoping |
| `microkit-messaging-release-manager` | Sonnet | `/microkit-messaging-release` — full release lifecycle |

---

## ⚡ Available Commands

| Command | Purpose |
|---------|---------|
| `/microkit-messaging-plan` | Run implementer agent — plan before any code |
| `/microkit-messaging-release` | Prepare and validate a release |
| `/microkit-messaging-new-provider` | Scaffold a new broker provider (RabbitMQ / ASB / Kafka) |

---

## 🔗 Context Layer

```
.claude-context/
├── standards/
│   ├── microkit-messaging-outbox-contracts.md   ← canonical outbox/inbox shapes (to create)
│   └── microkit-messaging-event-contracts.md    ← IIntegrationEvent format rules (to create)
├── templates/
│   ├── microkit-messaging-provider-template/    ← scaffold for new broker adapter (to create)
│   └── microkit-messaging-handler-template/     ← scaffold for new message handler (to create)
└── context/
    ├── microkit-messaging-architectural-decisions.md  ← ADRs (to create during implementation)
    └── microkit-messaging-dependency-graph.md         ← full dep graph with rationale (to create)
```

> All `.claude-context/` files marked "(to create)" are created during Phase 1 implementation.
> Agents load these files with `(if present)` — missing files are silently skipped and should
> not block planning.

---

## 🔢 Versioning

```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/messaging-v\\d+\\.\\d+\\.\\d+"
  ]
}
```

Git tag convention: `messaging-v1.0.0`, `messaging-v1.1.0-beta.1`
All v1 packages share one version per release.

---

## 🚀 Phase Status

| Package | Phase | Status |
|---------|-------|--------|
| `MicroKit.Messaging.Abstractions` | 1 | ✅ Implemented |
| `MicroKit.Messaging` | 1 | ✅ Implemented — processors, workers, coordinators, retention |
| `MicroKit.Messaging.EntityFrameworkCore` | 1 | ✅ Implemented — atomic claim + token-fenced settlement |
| `MicroKit.Messaging.MediatR` | 1 | ✅ Implemented — sink, routing dispatcher, cascade publisher |
| `MicroKit.Messaging.Testing` | 1 | 📋 Planned — **not built**; `src/` holds the four above (L0 #19) |
| `MicroKit.Messaging.RabbitMQ` | 2 | ⏳ Scaffold only (`IsPackable=false`) |
| `MicroKit.Messaging.AzureServiceBus` | 2 | ⏳ Scaffold only (`IsPackable=false`) |
| `MicroKit.Messaging.Kafka` | 2 | ⏳ Scaffold only (`IsPackable=false`) |
| `MicroKit.Messaging.OpenTelemetry` | 2 | ⏳ Scaffold only (`IsPackable=false`) |
| `MicroKit.Messaging.Serialization` | 2 | ⏳ Scaffold only (`IsPackable=false`) |

---

## 🔮 Key Architectural Decisions

- **ADR-MSG-001:** `IOutboxWriter`/`IOutboxProcessorStore` live in `Messaging.Abstractions` (not `Persistence.Abstractions`) — outbox is a messaging concern, not a persistence concern
- **ADR-MSG-002:** `IIntegrationEvent` used throughout (not `INotification`) — zero MediatR dependency in Abstractions, Core, EFCore and the broker providers; the `MicroKit.Messaging.MediatR` glue is the single carve-out (ADR-MSG-009). ⚠ Number collision: the architecture rule file uses ADR-MSG-002 for the Worker/Coordinator/Processor decomposition. Both are live; disambiguate by title, and reconcile the numbering before the next ADR is written
- **ADR-MSG-003:** Tenant-aware — `TenantId` is carried on every outbox/inbox row rather than in ambient context. **Not the same as non-nullable:** the column is `string?` and null is valid in single-tenant deployments (ADR-MSG-008 §5 settled this). ⚠ Number collision: the architecture rule file uses ADR-MSG-003 for the inbox delivery guarantee
- **ADR-MSG-004:** In-process transport is the v1 default — broker providers are v2 opt-in
- **ADR-MSG-005:** Background processors run under an `IHostedService` (`OutboxWorker` / `InboxWorker`, both `internal sealed`) with a lease for distributed safety. **The per-message lease it described is gone:** an atomic `ClaimBatchAsync` reserves a whole batch and stamps a `ClaimToken` that fences every terminal write (outbox rewrite #87, ADR-MSG-017)
- **ADR-MSG-006:** `OutboxMessage`/`InboxMessage` are `sealed class` (EF Core entities, mutable); `sealed record` is reserved for value objects. `IOutboxStore` split into `IOutboxWriter` (domain write) + `IOutboxProcessorStore` (processor read/write) to enforce ISP. `OutboxMessageStatus.Failed` always means terminal (DeadLettered=true) — there is no transient Failed state.
- **ADR-MSG-007:** lease acquisition uses a single `ExecuteUpdateAsync` (atomic UPDATE WHERE) — EF Core SELECT+mutate+SaveChanges is not atomic under concurrent processors and is forbidden. **The principle stands; the method does not:** `AcquireLeaseAsync` was deleted by the outbox claim rewrite and the inbox rewrite (ADR-MSG-017). The atomic write is now `ClaimBatchAsync`, which reserves a whole batch in one `UPDATE` and stamps a `ClaimToken` that every terminal write filters on.
- **ADR-MSG-010:** `IIntegrationEvent : IEvent` (canonical event taxonomy root from `MicroKit.Domain.Events`). Does NOT extend `IDomainEvent`. `MicroKit.Domain` dependency added to Abstractions.
- **ADR-MSG-011:** `IOutboxWriter.AddBatchAsync` ratified — batch write optimization for `DomainEventsDispatcher` P4 (single EF Core `AddRange` call). `AddAsync` kept for single-message paths.
- **ADR-MSG-012:** `DomainEventDispatchBehavior` SUPERSEDED — deleted in favour of `TransactionBehavior` (order 700) as the dispatch+commit owner.
- **ADR-MSG-013:** `DomainEventsCascadeNotificationPublisher` replaces `ForeachAwaitPublisher` — dispatches cascade domain events once after all notification handlers complete.
- **ADR-MSG-014:** `IOutboxCoordinator`, `IInboxCoordinator`, `IOutboxProcessor`, `IInboxProcessor` return `Task` (not `ValueTask`) — BackgroundService chain symmetry; no allocation benefit in polling loops. **Fully superseded as a return-type mandate** — by ADR-MSG-015 for the two OUTBOX seams and by ADR-MSG-017 for the two INBOX seams. All four now return `ValueTask<T>` with a batch result.
- **ADR-MEDIATR-014 / -015 (MicroKit.MediatR, implemented — this module is the other half):** the
  glue contributes an `IDomainEventsSink` to the single core dispatcher instead of registering a
  rival one, so registration order between the two packages no longer decides correctness.
  `AddMediatRTransport()` is renamed **`AddMediatRDomainEvents()`** (it is not a transport — the
  `Add{Provider}Transport()` shape stays reserved for brokers), the method is idempotent, and
  `AddInProcessTransport()` now uses `TryAdd` so a later transport registration cannot silently
  displace the `IOutboxDispatcher` decorator. Requires MicroKit.MediatR from the same release.
  **The `TryAdd` half is superseded by ADR-MSG-019**, which splits the seam into a keyed slot only
  Core writes and an unkeyed slot a decorator takes outright — so neither order can go wrong,
  rather than one of the two being made safe.
- **ADR-MSG-015:** `IOutboxCoordinator.ExecuteAsync` and `IOutboxProcessor.ProcessBatchAsync` return `ValueTask<OutboxBatchResult>` — the batch now produces a result the worker needs to adapt its cadence, and ADR-MSG-014's `.AsTask()` rationale was factually wrong. The inbox asymmetry it recorded was closed by ADR-MSG-017.
- **Step 5 (implemented):** `IIntegrationEventPublisher` now writes a `MessageKind.Contract` row into the **outbox**; `IntegrationEventMessage`, `IntegrationEventStatus` and their EF configuration are **deleted**, and re-introduction is blocked by `NoAssemblyStillCarriesTheDedicatedIntegrationEventTable`. `IIntegrationEventWriter.AddAsync` takes an `OutboxMessage`, returns `IntegrationEventWriteResult`, and **flushes inside the caller's transaction** (savepoint + post-hoc verification, the `EfInboxStore.AddAsync` pattern) so a replayed publication is absorbed before `PublishAsync` returns. `OutboxMessageFactory.Create` is renamed **`CreateNotification`** and gains **`CreateContract`**; `OutboxMessage` gains `TraceParent`; the claim now orders on **`CreatedAtUtc`**; retention refuses to purge a `Contract` row while its origin can still be dispatched.
- **ADR-MSG-018:** integration events are a **marker** contract — `IIntegrationEvent` loses every member and keeps only the `IEvent` base; the wire name moves to `[IntegrationEvent]`; metadata is assigned at staging from `IExecutionContext` onto ~~`IntegrationEventMessage`, which gets **its own table**~~ — **superseded by step 5: the dedicated table is retired and metadata lands on a `MessageKind.Contract` outbox row**. `IMessagePublisher`/`InProcessMessagePublisher` are **deleted** and ~~the in-process fan-out moves into `InProcessIntegrationDispatcher`~~, sourcing every field from the `OutboxMessage` row — which is what made the marker possible and closes a latent bug (the inbox dedup key was read off the event and survived redelivery only by accident). Publishing requires an open transaction the caller owns: `IUnitOfWork.CommitAsync` alone is **not** one. Also fixes L0 #21 — `IExecutionContext` now resolves through a scoped holder, so constructor injection finally sees the message row.
  **⚠ Superseded in part by ADR-MSG-019 — three points, no more.** The fan-out was *withdrawn*, not relocated: `InProcessIntegrationDispatcher` does not exist, and the row-is-the-source-of-metadata reasoning is now carried by `TransportOutboxDispatcher`. Also superseded: the claim that `AddInProcessTransport()` keeps its registrations, and the rejected alternative that cited `InboxRedeliveryTests` as blocking evidence. **Everything else above stands.**
- **ADR-MSG-019:** the reentrant outbox. One table, two natures of row, **routed by `MessageKind`
  and never by a CLR type test**. `MicroKit.Messaging.MediatR` **decorates** rather than replaces:
  `Notification` → `IPublisher.Publish`, everything else → an **optional** inner resolved from the
  keyed `OutboxDispatcherKeys.Standard`, so registration order cannot bypass it and a
  notification-only host composes with no transport at all. `InProcessIntegrationDispatcher` and
  `AddInProcessTransport()` are **deleted**; the `IMessageSerializer` default moves to
  `AddMicroKitMessaging()`. It left the inbox without a producer; **step 7 closed that** —
  `IEnvelopeReceiver` writes the rows on the receiving side, `InboxIngestionValidator` is deleted,
  and `AddMessageHandler<,>` no longer fails at boot (see the ADR's step-7 implementation note).
  Also records
  per-message settlement, the `(OriginMessageId, ContractName)` natural key, the abandonment of
  `IOutboxSettlementStore` (fan-out makes the target transaction ambiguous), and the registry's
  reversal from publishing-only to bidirectional.
- **ADR-MSG-017:** the inbox rewrite. Atomic `ClaimBatchAsync` + token-fenced `ApplyOutcomesAsync` replace the per-message lease; the primary key moves to a `RowId` surrogate with the compound key surviving as the unique dedup index (a compound-key claim selected a CROSS PRODUCT and could exceed `batchSize` several times over); **success settles inside the handler's own transaction** via `IInboxSettlementStore`, which is why the inbox is NOT a mirror of the outbox; `ClaimToken` is an EF concurrency token, without which the ownership mechanism is decorative; `IInboxWriter.AddAsync` returns `InboxWriteResult` instead of throwing on a redelivery — the defect that dead-lettered correctly delivered messages. Closes the inbox half of ADR-MSG-014.

---

## 🤖 Immutable Flow (agents)

```
PRE-CODE  : implementer /plan → architect review → implementation
POST-CODE : distributed-context-specialist (if AsyncLocal / background worker / hosted service)
            dependency-guardian (if .csproj modified)
            api-reviewer (if public API changed)
            → in the same Claude Code session
            → "Do not commit anything" mandatory in all post-code prompts
MERGE     : only after all relevant agents approved
/compact  : after full package implementation, before new session
```
