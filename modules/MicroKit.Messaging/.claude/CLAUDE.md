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
MicroKit.Messaging            ← transactional outbox, inbox dedup, in-process transport
        │
        ├── OutboxProcessor (IHostedService)   ← polls and dispatches pending messages
        ├── InboxProcessor  (IHostedService)   ← deduplicates and routes inbound messages
        └── IMessagePublisher                  ← abstraction over broker transport
                │
                ├── InProcessMessagePublisher  ← v1 default — in-process only
                ├── RabbitMqMessagePublisher   ← v2 provider (planned)
                ├── AzureServiceBusPublisher   ← v2 provider (planned)
                └── KafkaMessagePublisher      ← v2 provider (planned)
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
│   ├── MicroKit.Messaging.Abstractions/        ← IIntegrationEvent, IMessagePublisher,
│   │                                              IMessageHandler<T>, IOutboxWriter,
│   │                                              IOutboxProcessorStore, IInboxStore,
│   │                                              OutboxMessage (sealed class), InboxMessage (sealed class),
│   │                                              MessageEnvelope<T> (sealed record)
│   ├── MicroKit.Messaging/                     ← OutboxProcessor, InboxProcessor,
│   │                                              InProcessMessagePublisher, MessageDispatcher,
│   │                                              DI extensions, background workers
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
IIntegrationEvent                  // typed contract — all integration events implement this;
                                   //   defines TenantId, CorrelationId, CausationId, OccurredOnUtc
MessageId                          // sealed record — strongly-typed message identifier
CorrelationId                      // sealed record — correlation chain identifier
CausationId                        // sealed record — causal parent identifier (nullable on root events)
```

### Publishing
```csharp
IMessagePublisher                  // PublishAsync<T>(T evt, CancellationToken ct) → ValueTask
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
IOutboxProcessorStore              // GetPendingAsync, AcquireLeaseAsync, MarkPublishedAsync,
                                   //   MarkFailedAsync, DeadLetterAsync, DeleteProcessedAsync,
                                   //   GetDeadLetteredAsync, RequeueAsync — background processor only
IInboxStore                        // ExistsAsync, AddAsync, GetPendingAsync, MarkProcessingAsync,
                                   //   MarkProcessedAsync, MarkFailedAsync
```

### Outbox / Inbox messages
```csharp
OutboxMessage                      // sealed class — EF Core entity; Id, TenantId, EventType, Payload,
                                   //   Status, RetryCount, LockedUntilUtc, NextRetryAtUtc, DeadLettered, ...
InboxMessage                       // sealed class — EF Core entity; MessageId, ConsumerType, Status, ...
MessageEnvelope<T>                 // sealed record — wraps T with metadata (MessageId, CorrelationId, ...)
```

---

## 📐 Non-Negotiable Rules

1. **`IIntegrationEvent` (not `INotification`)** — no MediatR dependency anywhere in Messaging
2. **`IOutboxWriter` and `IOutboxProcessorStore` live in `Messaging.Abstractions`** — never in `Persistence.Abstractions`
3. **Tenant-aware mandatory** — `TenantId` on `OutboxMessage` and `InboxMessage` — never null
4. **Outbox states** — `Pending → Processing → Published` or `Failed+DeadLettered=true`; **`Failed` always means terminal** (DeadLettered=true)
5. **Inbox dedup key** = `(MessageId + ConsumerType)` — compound PK unique constraint is the real guard
6. **No silent success when publisher is null** — throw `InvalidOperationException`, not fake success
7. **Background processors never use `IHttpContextAccessor`** — `TenantId` read from `OutboxMessage`/`InboxMessage` only
8. **`sealed class`** for EF Core entities (`OutboxMessage`, `InboxMessage`) | **`sealed record`** for VOs (`MessageId`, `CorrelationId`, `CausationId`, options) | **`sealed class`** for processors/handlers/publishers
9. **`ValueTask<T>`** for all async methods | **`ConfigureAwait(false)`** throughout lib code
   **Exception (ADR-MSG-014):** `IOutboxCoordinator.ExecuteAsync`, `IInboxCoordinator.ExecuteAsync`,
   `IOutboxProcessor.ProcessBatchAsync`, `IInboxProcessor.ProcessBatchAsync` return `Task` —
   BackgroundService chain compatibility; no allocation benefit from ValueTask in polling loops.
10. **`CancellationToken ct = default`** always last parameter
11. **`Console.WriteLine` forbidden** → `ILogger<T>`
12. **No inline `Version=`** on `PackageReference` — CPM via root `Directory.Packages.props`
13. **XML docs on all public members** in `src/` projects
14. **`MediatR.Contracts` forbidden everywhere** — in all packages, production and test, **except the `MicroKit.Messaging.MediatR` glue** (ADR-MSG-009 carve-out: the glue bridges domain-event notifications onto the outbox via `IPublisher.Publish`)
15. **`FluentAssertions` forbidden** — use Shouldly (MIT)
16. **Scope-per-message mandatory** in `OutboxProcessor` and `InboxProcessor` — never share one scope across a batch
17. **`AcquireLeaseAsync` must be atomic** — single `UPDATE WHERE` via `ExecuteUpdateAsync`; SELECT+mutate+SaveChanges is forbidden

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
| `MicroKit.Messaging.Abstractions` | 1 | ✅ Implemented — build verified, all agents approved |
| `MicroKit.Messaging` | 1 | 📋 Planned |
| `MicroKit.Messaging.EntityFrameworkCore` | 1 | 📋 Planned |
| `MicroKit.Messaging.Testing` | 1 | 📋 Planned |
| `MicroKit.Messaging.RabbitMQ` | 2 | ⏳ Scaffold only (`IsPackable=false`) |
| `MicroKit.Messaging.AzureServiceBus` | 2 | ⏳ Scaffold only (`IsPackable=false`) |
| `MicroKit.Messaging.Kafka` | 2 | ⏳ Scaffold only (`IsPackable=false`) |
| `MicroKit.Messaging.OpenTelemetry` | 2 | ⏳ Scaffold only (`IsPackable=false`) |
| `MicroKit.Messaging.Serialization` | 2 | ⏳ Scaffold only (`IsPackable=false`) |

---

## 🔮 Key Architectural Decisions

- **ADR-MSG-001:** `IOutboxWriter`/`IOutboxProcessorStore` live in `Messaging.Abstractions` (not `Persistence.Abstractions`) — outbox is a messaging concern, not a persistence concern
- **ADR-MSG-002:** `IIntegrationEvent` used throughout (not `INotification`) — zero MediatR dependency
- **ADR-MSG-003:** Tenant-aware mandatory — `TenantId` is non-negotiable on all outbox/inbox rows
- **ADR-MSG-004:** In-process transport is the v1 default — broker providers are v2 opt-in
- **ADR-MSG-005:** Background processors use `IHostedService` with lease/lock pattern for distributed safety
- **ADR-MSG-006:** `OutboxMessage`/`InboxMessage` are `sealed class` (EF Core entities, mutable); `sealed record` is reserved for value objects. `IOutboxStore` split into `IOutboxWriter` (domain write) + `IOutboxProcessorStore` (processor read/write) to enforce ISP. `OutboxMessageStatus.Failed` always means terminal (DeadLettered=true) — there is no transient Failed state.
- **ADR-MSG-007:** `AcquireLeaseAsync` uses a single `ExecuteUpdateAsync` (atomic UPDATE WHERE) — EF Core SELECT+mutate+SaveChanges is not atomic under concurrent processors and is forbidden for lease acquisition.
- **ADR-MSG-010:** `IIntegrationEvent : IEvent` (canonical event taxonomy root from `MicroKit.Domain.Events`). Does NOT extend `IDomainEvent`. `MicroKit.Domain` dependency added to Abstractions.
- **ADR-MSG-011:** `IOutboxWriter.AddBatchAsync` ratified — batch write optimization for `DomainEventsDispatcher` P4 (single EF Core `AddRange` call). `AddAsync` kept for single-message paths.
- **ADR-MSG-012:** `DomainEventDispatchBehavior` SUPERSEDED — deleted in favour of `TransactionBehavior` (order 700) as the dispatch+commit owner.
- **ADR-MSG-013:** `DomainEventsCascadeNotificationPublisher` replaces `ForeachAwaitPublisher` — dispatches cascade domain events once after all notification handlers complete.
- **ADR-MSG-014:** `IOutboxCoordinator`, `IInboxCoordinator`, `IOutboxProcessor`, `IInboxProcessor` return `Task` (not `ValueTask`) — BackgroundService chain symmetry; no allocation benefit in polling loops.

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
