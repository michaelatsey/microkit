# MicroKit.Messaging — Module Brain

## Purpose

A message bus — publish, consume, retry — with a transactional outbox and an idempotent inbox as
**opt-in durability**, and a broker-agnostic transport seam. The bus works with no database; a
**store** (the outbox and the inbox of one `DbContext`) makes it durable. The user-facing model is
`README.md`; the decisions are `.claude-context/context/microkit-messaging-architectural-decisions.md`
(ADR-MSG-020 is the current model, #188; ADR-MSG-021 adds partitioned stores, #191).

```
IMessageBus.PublishAsync ──► no store: IMessageTransport (#186)
                         └─► store:    outbox row enlisted in the store's DbContext (never saved here)
                                           │ the store's outbox loop
                                           ├─ Endpoint null    → TransportOutboxDispatcher → IMessageTransport
                                           └─ Endpoint "local" → LocalOutboxDispatcher → receive pipeline

IEnvelopeReceiver / InMemoryTransport / local delivery / inbox retry
        └─► MessageReceivePipeline ── per handler: inbox guard (store) or plain scope (no store)
```

---

## Navigation

| Task | Load first |
|------|-----------|
| Anything in the engine, the stores, the guard | `.claude/rules/microkit-messaging-outbox-inbox.md` |
| Layering, packages, module boundaries | `.claude/rules/microkit-messaging-architecture.md` |
| Public API or a name | `.claude/rules/microkit-messaging-naming.md` + `tests/MicroKit.Messaging.ArchitectureTests/PublicSurfaceTests.cs` |
| A `.csproj` change | `.claude/rules/microkit-messaging-dependencies.md` |
| Tests | `.claude/rules/microkit-messaging-testing.md`, `.claude/skills/microkit-messaging-testing/SKILL.md` |
| A decision | the ADR file above |

The agents in `.claude/agents/` and the commands in `.claude/commands/` are listed there; the root
`.claude/CLAUDE.md` says when a review agent runs.

---

## Module structure

```
src/
  MicroKit.Messaging.Abstractions/        namespace MicroKit.Messaging — the application surface:
                                            IIntegrationEvent, MessageContractAttribute, IMessageBus,
                                            IEventPublisher, PublishOptions, MessageStoreKey,
                                            IMessageHandler<T>, MessageContext, the middlewares, exceptions
                                          namespace MicroKit.Messaging.Spi — what a store, transport or
                                            integration package implements: IMessageTransport,
                                            MessageEnvelope, IEnvelopeReceiver, ReceiveDisposition, the
                                            store interfaces (IOutboxWriter, IOutboxProcessorStore,
                                            IInboxGuardStore, IInboxProcessorStore, admin, retention),
                                            IMessageStoreSet, ILocalMessageSender, ILocalConsumerSource,
                                            LocalConsumer, OutboxMessage, InboxMessage
  MicroKit.Messaging/                     the bus (Bus/), the receive pipeline and inbox guard, the
                                            in-memory transport (Transport/), the store engine
                                            (Processing/: MessageStoreEngine, Outbox/Inbox loops,
                                            processors, IOutboxDispatcher — internal), the local endpoint
                                            (Local/), composition (DI/, Stores/: AddMessageStore,
                                            MessageStoreBuilder, MessagingIntegrationExtensions)
  MicroKit.Messaging.EntityFrameworkCore/ the EF Core store: UseEntityFrameworkOutbox<TContext>(),
                                            ApplyMessagingConfiguration(), EfOutboxStore, EfInboxStore
  MicroKit.Messaging.MediatR/             UseMediatRDomainEvents(): the sink, the dispatch middleware,
                                            the notification handler source — on the public SPI only
tests/
  MicroKit.Messaging.UnitTests, .IntegrationTests (SQLite + PostgreSql/ via Testcontainers),
  .IntegrationTests.SecondModule (a second assembly for store binding), .ArchitectureTests,
  .MediatR.UnitTests, .MediatR.IntegrationTests (SQLite + PostgreSql/)
```

No broker transport ships: a provider package supplies an `IMessageTransport` and drives a consume
loop into `IEnvelopeReceiver`.

---

## Dependencies (as the .csproj files declare them)

```
MicroKit.Messaging.Abstractions      ← MicroKit.Result, MicroKit.Domain (IIntegrationEvent : IEvent)
MicroKit.Messaging                   ← Abstractions, MicroKit.Execution.Abstractions,
                                       Microsoft.Extensions.{DependencyInjection,Hosting,Logging}.Abstractions,
                                       Microsoft.Extensions.Options(.ConfigurationExtensions)
MicroKit.Messaging.EntityFrameworkCore ← MicroKit.Messaging, MicroKit.Persistence.EntityFrameworkCore,
                                       Microsoft.EntityFrameworkCore.Relational
MicroKit.Messaging.MediatR           ← MicroKit.Messaging, MicroKit.MediatR, MicroKit.Domain
```

- **MediatR is referenced by `MicroKit.Messaging.MediatR` only** (ADR-MSG-009), and by its tests.
- **Messaging does not depend on Tenancy**: a tenant-aware `IExecutionScopeFactory` is composed by
  the host (ADR-EXEC-001).
- The root dependency graph and these edges disagree on `MicroKit.Domain`; reconciliation is #151.
- `InternalsVisibleTo` from `MicroKit.Messaging` goes to its own test projects only (and
  `DynamicProxyGenAssembly2` for NSubstitute): the MediatR glue has no access to Core's internals.

---

## Non-negotiable rules

1. **Never succeed silently.** A publication either reaches the transport or a store's outbox, or
   throws. A row enlisted and never saved is reported (event 4000). A message no handler consumes
   is reported (3001, `microkit.inbox.envelopes.unconsumed`). A composition fault fails at start,
   naming its fix.
2. **The bus never saves.** With a store, a publication is enlisted in the store's `DbContext` of the
   scope; the caller's `SaveChanges` — or, inside a delivery, the engine's — commits it.
3. **The store instance and key come from the scope**, never from a type fixed at registration
   (partitioned stores rely on it, ADR-MSG-021). Store resolution follows ADR-MSG-020 §3 exactly.
4. **One inbox row per (MessageId, ConsumerType)**, the unique index the sole authority. An existing
   row skips a new delivery; an attempt runs as one operation of the store's execution strategy; an
   unknown commit is verified by reading the row with no tracking; a failure is written on its row in
   its own transaction before the delivery is acknowledged.
5. **Every terminal write filters on `ClaimToken`**, and `ClaimToken` is an EF concurrency token on
   the inbox. A lost claim writes zero rows and counts no attempt.
6. **A host stop is never a failure**: claimed rows are released with their attempt count unchanged.
   Every long-running loop runs under `GuardedLoop`: only a host stop ends it — or, for an
   instance's loop, its instance's removal, read between batches (ADR-MSG-021) — an
   `ObjectDisposedException` while the host runs, a throwing logger or a configuration fault (1402)
   is a failed cycle with back-off.
7. **Activation is not registration**: a missing registration fails at start; a registered service
   whose constructor throws is logged as an activation failure (1400), with back-off.
8. **One scope per handler**, never shared; one loop per store, or per instance of a partitioned
   store, each with its own back-off. A partitioned store's instance scopes are prepared by its
   `IMessageStoreInstanceInitializer` before anything is resolved; its instances' batches pass one FIFO
   gate, which a single store never enters.
9. **`TenantId` is nullable and travels on the message**, never read from an HTTP context.
10. **Causation is derived** (the message being handled), correlation is copied.
11. Code: `sealed record` for values/options/events, `sealed class` for services; `ValueTask` and
    `ConfigureAwait(false)`; `CancellationToken ct = default` last; `[LoggerMessage]` logging, no
    `Console.WriteLine`; XML docs on every public member; frozen collections for registries built at
    start; no per-message closures or LINQ on the hot paths.
12. Tests: xUnit, Shouldly, NSubstitute, NetArchTest, Testcontainers.PostgreSql. FluentAssertions is
    forbidden. Anything touching uniqueness or concurrency runs on PostgreSQL. A fix comes with a
    test shown red when the fix is reverted.

---

## Build and test

```bash
dotnet build modules/MicroKit.Messaging/MicroKit.Messaging.slnx -c Release
CI=true dotnet test modules/MicroKit.Messaging/MicroKit.Messaging.slnx -c Release
```

`CI=true` makes `[DockerRequiredFact]` run — and fail — when no Docker daemon answers; without it the
PostgreSQL suites skip on a machine with no Docker.

---

## Known defects and owed work

- Several hosts each drain every instance of a partitioned store (correct through the atomic claim);
  one engine instance per store is #214. A commit wakes its store's outbox loop on its own host only
  (#215, ADR-MSG-022); a host scope's commit to a partitioned store waits for the next poll (#189).
- Versioning and releases: ADR-GLOBAL-002.
