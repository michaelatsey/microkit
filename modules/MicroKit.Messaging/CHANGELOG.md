# Changelog — MicroKit.Messaging

## [Unreleased] — outbox and inbox claim rewrites

Both sides of the module move off the per-message lease and onto an atomic batch claim carrying an
ownership token. Round trips per batch drop from `2N+1` to two or three plus one settlement — flat
in `N` — on the outbox, and to three on the inbox.

**The inbox is deliberately not a mirror of the outbox**, and that is the whole of its design. An
outbox may batch its settlement: widening the crash window there widens only duplication, and the
inbox absorbs it downstream. For the inbox there is no downstream — the inbox *is* the absorber, so
a crash between a handler returning and its row being marked reruns the handler with its business
side effects. Inbox **success** therefore settles inside the handler's own transaction; only
failures and releases are batched.

See ADR-MSG-015 (outbox) and ADR-MSG-017 (inbox).

### Fixed
- **Lost update under lease expiry.** `MarkPublishedAsync` / `MarkFailedAsync` / `DeadLetterAsync`
  filtered on `m.Id` alone, so a processor whose lease expired mid-dispatch silently overwrote the
  processor that legitimately re-claimed the message. Every terminal write now also filters on
  `ClaimToken`; a lost lease yields zero rows.
- **Silent success.** Those methods returned `Result.Success()` unconditionally, discarding the
  affected-row count. `ApplyOutcomesAsync` returns the row count and the processor logs a partial
  settlement; `RequeueAsync` returns `bool`.
- **Retention unreachable in single-tenant deployments.** `DeleteProcessedAsync` and
  `GetDeadLetteredAsync` took a non-nullable `tenantId` compared against a nullable `TenantId`, so
  rows with a null tenant never matched and the outbox grew without bound. Both now take `string?`,
  with `null` meaning every tenant.
- **Retention had no caller at all.** `DeleteProcessedAsync` was invoked by nothing and
  `RetentionDays` was read by nothing. `OutboxRetentionWorker` now runs one pass on a slow timer.
- **A missing `IOutboxDispatcher` registration burned the whole queue's retry budget.** It threw per
  message and was classified transient. It is now caught structurally as
  `OutboxConfigurationException`: the batch is settled with every message released, then the fault
  is rethrown and the worker stops.
- **Poison messages consumed the full retry budget** for a verdict reached on the first attempt.
  `OutboxPayloadException` dead-letters on first sight.
- **A broker outage failed all N messages, wrote N failure rows and consumed N retry budgets**, then
  repeated next tick. `OutboxTransportUnavailableException` abandons the batch and releases the
  remainder as `Released` — no retry consumed.
- **`Action<OutboxProcessorOptions>` was a silent no-op.** `OutboxProcessorOptions` had `init`-only
  accessors, so the configuration callback taken by `AddMicroKitMessaging` could not assign
  anything. Accessors are now `{ get; set; }`.
- **Deterministic back-off caused synchronised retries.** With several processor instances, messages
  that failed together retried together. Back-off is now
  `Uniform(0, min(2^RetryCount s, MaxRetryBackoff))` — full jitter.

### Changed — BREAKING (MicroKit.Messaging.MediatR)
- **`AddMediatRTransport()` is renamed `AddMediatRDomainEvents()`, with no `[Obsolete]` alias.** The
  old name was wrong twice: it transports nothing, and `Add{Provider}Transport()` is reserved by this
  module's naming rules for broker providers (`AddRabbitMqTransport`). The method contributes the
  outbox `IDomainEventsSink`, decorates `IOutboxDispatcher`, and replaces `INotificationPublisher` —
  three things, none of them a transport. Both packages are `1.0.0-preview.*` with zero external
  consumers, so the rename ships outright rather than accumulating a permanent alias.
  **Migration:** rename the call. Nothing else changes — same receiver, same signature, same
  position in the chain (ADR-MEDIATR-015).
- **The glue no longer registers an `IDomainEventsDispatcher`.** It contributes an
  `IDomainEventsSink` to the single core orchestrator in `MicroKit.MediatR` instead of registering a
  rival dispatcher, so the two packages can no longer disagree about which implementation wins —
  the race is gone rather than arbitrated. `DomainEventsDispatcher` becomes `OutboxDomainEventSink`
  and sheds the drain and handler-dispatch phases it used to duplicate; both types are
  `internal sealed`, so **no consumer-visible type changed**. **This requires MicroKit.MediatR from
  the same release** — the glue will not compile against a core without `IDomainEventsSink`
  (ADR-MEDIATR-014).

### Fixed
- **`AddInProcessTransport()` called after the MediatR glue silently disabled notification
  publishing.** It registered `IOutboxDispatcher`, `IMessagePublisher` and `IMessageSerializer` with
  a plain `Add`, so a later call appended a second `IOutboxDispatcher` descriptor; Microsoft DI
  resolves the last one, and `MediatROutboxDispatcher` was bypassed with no exception and no log.
  The outbox kept draining and reporting success while every domain-event notification went
  unpublished. All three registrations now use `TryAdd` — a transport supplies a default and
  abstains if something already holds the slot. The duplicate `IMessageSerializer` descriptor that
  stacked for the same reason is gone too.
- **Calling the MediatR glue twice double-wrapped the outbox dispatcher.** Its `LastOrDefault`
  descriptor hunt found its own factory registration on the second call and decorated it again,
  routing every outbox message through two decorators. The method is now idempotent: one sink
  (`TryAddEnumerable`), one decorator, one publisher replacement, however many times it is called
  (ADR-MEDIATR-015).

### Added
- `MicroKit.Messaging/README.md` — the module had none. Covers what the packages do, the complete DI
  composition that works, one end-to-end example, and what is stable versus still moving.
- `OutboxClaim`, `OutboxOutcome` + `OutboxOutcomeKind`, `OutboxBatchResult` + `OutboxBatchAbortReason`.
- `OutboxPayloadException`, `OutboxTransportUnavailableException`, `OutboxConfigurationException`.
- `IOutboxAdminStore` and `IOutboxRetentionStore` — split out of `IOutboxProcessorStore` (ISP).
- `OutboxMessage.ClaimToken` (`Guid?`), plus `IX_OutboxMessages_Dispatchable` and
  `IX_OutboxMessages_ClaimToken`.
- `IComparable<T>` on `MessageId`, `CorrelationId` and `CausationId` — the claim sorts candidate ids
  for deterministic lock ordering, and a positional record derives no ordering.
- Adaptive worker cadence, driven by `OutboxBatchResult`.
- `OutboxRetentionWorker`.
- `OutboxProcessorOptions`: `MaxPollingInterval`, `TransportUnavailableBackoff`, `MaxRetryBackoff`,
  `OutcomeFlushTimeout`, `MaxErrorMessageLength`, `RetentionInterval`.
- `TimeProvider` and `Random` registered via `TryAddSingleton`, injected into the processor and
  store so back-off is testable without a wall clock.

### Fixed — inbox

- **A redelivery dead-lettered a correctly delivered message.** `IInboxStore.AddAsync` reported its
  *nominal* outcome — "already present, nothing done" — by throwing. Under at-least-once delivery a
  redelivery needs no failure at all; one expired lease after a crash produces it. Nothing caught
  the exception, so it reached `OutboxProcessor`, was classified a transient dispatch failure, and
  retried into the same duplicate until the message dead-lettered. `IInboxWriter.AddAsync` now
  returns `InboxWriteResult`, and `InProcessMessagePublisher` treats `AlreadyPresent` as a
  successful skip.
- **A duplicate on one consumer silently lost the rows of every later consumer.** One event fans out
  to one row per consumer; the escaping exception ended the whole publish, so consumers 3..N never
  got their row at all — a partial redelivery becoming permanent loss. The publisher now
  `continue`s.
- **`MarkProcessingAsync` was not a lease.** No eligibility predicate, `void` return: an
  unconditional write. Two processors could both "acquire" the same row, both invoke the handler,
  and neither could tell — in the component whose entire job is deduplication. Replaced by
  `ClaimBatchAsync`, which replays the eligibility predicate inside a single `UPDATE`.
- **Lost update on every terminal inbox write.** `MarkProcessedAsync` / `MarkFailedAsync` /
  `DeadLetterAsync` filtered on the compound key alone, so a processor whose lease had expired
  overwrote the one that legitimately re-claimed the row. Every write now filters on `ClaimToken`.
- **Silent success.** All three returned `Result.Success()` regardless of affected rows.
  `ApplyOutcomesAsync` returns the row count and the processor logs a partial settlement;
  `RequeueAsync` returns `bool`.
- **A missing handler registration killed the whole batch.** Handler resolution sat outside the
  `try`, so it threw straight out of `ProcessBatchAsync`: every lease stranded until expiry, and the
  worker classified it transient and retried forever. It is now caught structurally as
  `InboxConfigurationException`; the batch is settled with every row released, then the fault is
  rethrown and the worker stops.
- **Two permanent failures burned the full retry budget.** An unregistered `ConsumerType` and a
  payload that will not deserialize each took `MaxRetries` attempts with exponential back-off for a
  verdict fixed at the first. Both now raise `InboxPayloadException` and dead-letter on first sight.
- **An unknown consumer settled an unleased row.** The registry check ran before any lease was
  taken, yet the failure policy wrote anyway. The check now runs inside the claimed batch, so every
  write is under a live claim.
- **A downstream outage failed all N rows, wrote N failure rows and consumed N retry budgets**, then
  repeated next tick. `InboxDependencyUnavailableException` abandons the batch and releases the
  remainder as `Released` — no retry consumed.
- **`AddAsync` poisoned the context on a duplicate.** The rejected entity stayed tracked as added, so
  the next `SaveChangesAsync` retried the same failing insert — one duplicate broke every later
  write on that context. It is detached before rethrowing.
- **Inbox retention was unreachable and had no caller.** There was no `DeleteProcessedAsync` at all,
  so the table grew without bound. `IInboxRetentionStore` and `InboxRetentionWorker` now exist, and
  `GetDeadLetteredAsync` takes `string?` so a single-tenant deployment — where every row has a null
  tenant — matches.
- **`Action<InboxProcessorOptions>` was a silent no-op**, the same `init`-only defect the outbox
  options carried. Accessors are now `{ get; set; }`.
- **The transactional guarantee silently did not exist under `QueryTrackingBehavior.NoTracking`.**
  `StageProcessedAsync` did not state its tracking mode — the one query in `EfInboxStore` that
  needs tracking, while the other four all say `AsNoTracking()`. On a consumer `DbContext` with
  that ordinary global setting the row came back untracked, the mark reached nothing, and
  `IsMarkUncommitted` reported it committed: the batch returned `Processed` while the row kept its
  claim token and replayed on every pass, never incrementing `RetryCount` and never
  dead-lettering. Now `AsTracking()`, and pinned by `InboxSettlementGuaranteeTests`.
- **`IsMarkUncommitted` read a discarded mark as a committed one.** An absent or detached entry
  was treated as "nothing pending, therefore saved" — literally true and operationally wrong: a
  handler calling `ChangeTracker.Clear()` throws the staged mark away, and nothing is pending
  precisely because the write was discarded. Absent now means uncommitted, so the failure costs a
  redundant deferred write and a warning instead of a silent infinite replay.
- **The inbox had no jitter at all.** Back-off is now
  `Uniform(0, min(2^RetryCount s, MaxRetryBackoff))`, computed by the processor rather than the
  store so it is testable without a database.

### Added — inbox

- `InboxClaim`, `InboxOutcome` + `InboxOutcomeKind`, `InboxBatchResult` + `InboxBatchAbortReason`,
  `InboxMessageKey`, `InboxWriteResult`.
- `InboxPayloadException`, `InboxDependencyUnavailableException`, `InboxConfigurationException`.
- `IInboxWriter`, `IInboxProcessorStore`, `IInboxSettlementStore`, `IInboxAdminStore`,
  `IInboxRetentionStore` — `IInboxStore` split five ways (ISP).
- `InboxMessage.RowId` (`Guid`, the new primary key) and `InboxMessage.ClaimToken` (`Guid?`), plus
  `UX_InboxMessages_MessageId_ConsumerType`, `IX_InboxMessages_Processable`,
  `IX_InboxMessages_ClaimToken` and `IX_InboxMessages_TenantId_ProcessedAt`.
- Adaptive inbox worker cadence, driven by `InboxBatchResult`.
- `InboxRetentionWorker`, and `InboxMetrics` (`microkit.inbox.messages.added` /
  `.deduplicated`; subscribe with `AddMeter(InboxMetrics.MeterName)`).
- `InboxProcessorOptions`: `MaxPollingInterval`, `DependencyUnavailableBackoff`, `MaxRetryBackoff`,
  `OutcomeFlushTimeout`, `MaxErrorMessageLength`, `RetentionDays`, `RetentionInterval`.
- `TimeProvider` and `Random` injected into `InboxProcessor`; `TimeProvider` into
  `EfInboxStore<TContext>`.

### Changed — BREAKING (inbox)

1. `IInboxStore` is **removed**, split into `IInboxWriter`, `IInboxProcessorStore`,
   `IInboxSettlementStore`, `IInboxAdminStore` and `IInboxRetentionStore`.
2. `IInboxWriter.AddAsync` returns `ValueTask<InboxWriteResult>` instead of `ValueTask`.
   **This breaks fewer callers than it looks.** `await writer.AddAsync(message, ct);` compiles
   unchanged — discarding the value of an awaited expression used as a statement is legal C# and
   raises no diagnostic, even under `TreatWarningsAsErrors`. Only a caller that assigned the
   returned `ValueTask` to a variable, or converted it with `.AsTask()`, has to change.
   So **nothing in the type system stops you ignoring the result**, and an earlier draft of this
   note claimed otherwise. If you ignore it, a rising deduplication rate — the signal of a lease
   set too short or a consumer stalling — becomes invisible in your logs. The enforcement that
   actually exists is `InboxMetrics`: `microkit.inbox.messages.deduplicated` is recorded on the
   ingestion path whether or not the caller inspects the return value. Subscribe to it.
3. `IInboxProcessor.ProcessBatchAsync` returns `ValueTask<InboxBatchResult>` (ADR-MSG-017).
4. `IInboxCoordinator.ExecuteAsync` returns `ValueTask<InboxBatchResult>` (ADR-MSG-017). Together
   with (3) this closes the inbox half of ADR-MSG-014, which ADR-MSG-015 left dated as debt.
5. `InboxProcessor` and `EfInboxStore<TContext>` take new constructor dependencies
   (`TimeProvider`, and `Random` for the processor).
6. `InboxMessage` gains `RowId` and `ClaimToken`, and **the primary key moves to `RowId`** — see
   the migration below, which is not optional.
7. `InboxProcessorOptions` — `init` accessors become `set`.
8. **`BatchSize` 20 → 100 and `MaxRetries` 10 → 5 are behavioural changes**, not merely new
   defaults. Both bind unchanged from existing configuration but halve the retry budget.
9. `InboxProcessorOptions.RetentionDays` defaults to **30, not the outbox's 7, and the asymmetry is
   deliberate.** On the outbox, deleting early loses history; on the inbox it loses the
   deduplication guarantee, because the table only deduplicates messages it still holds. The window
   must exceed the maximum plausible redelivery delay of every upstream transport.

### Migration — inbox schema

**Two structural changes, not one: the claim token *and* the primary key.** A consumer who applies
only `claim_token` gets a schema the code cannot query.

**Drain the inbox before migrating.** Stop the workers and let the table empty. Rows sitting in
`Processing` when the primary key changes are the one case with no clean answer. On a table of any
size steps 1 and 3 take an `ACCESS EXCLUSIVE` lock.

```sql
-- 1. Surrogate key. Populate existing rows before making it NOT NULL.
ALTER TABLE inbox_messages ADD COLUMN row_id uuid;
UPDATE inbox_messages SET row_id = gen_random_uuid() WHERE row_id IS NULL;
ALTER TABLE inbox_messages ALTER COLUMN row_id SET NOT NULL;

-- 2. Recreate the dedup gate BEFORE dropping the primary key, so the table is never left
--    without one. Dropping first would leave a window, however short, in which duplicate
--    ingestion is accepted. A redundant unique index alongside the PK is allowed.
CREATE UNIQUE INDEX ux_inbox_messages_message_id_consumer_type
    ON inbox_messages (message_id, consumer_type);

-- 3. Swap the primary key. Confirm the existing constraint name first:
--      SELECT conname FROM pg_constraint
--      WHERE conrelid = 'inbox_messages'::regclass AND contype = 'p';
ALTER TABLE inbox_messages DROP CONSTRAINT pk_inbox_messages;
ALTER TABLE inbox_messages ADD  CONSTRAINT pk_inbox_messages PRIMARY KEY (row_id);

-- 4. Claim token.
ALTER TABLE inbox_messages ADD COLUMN claim_token uuid NULL;
CREATE INDEX ix_inbox_messages_claim_token ON inbox_messages (claim_token);

-- 5. Claim path, ordered by received_at_utc.
CREATE INDEX ix_inbox_messages_processable
    ON inbox_messages (dead_lettered, status, next_retry_at_utc, received_at_utc);

-- 6. Retention.
CREATE INDEX ix_inbox_messages_tenant_id_processed_at
    ON inbox_messages (tenant_id, processed_at_utc);
```

> The EF Core configuration ships **unfiltered** indexes because `HasFilter` takes
> provider-specific SQL and `MicroKit.Messaging.EntityFrameworkCore` is the provider-neutral
> package. A consumer writing their own DDL should prefer the partial forms
> (`WHERE claim_token IS NOT NULL`, `WHERE dead_lettered = false`).

### Changed — BREAKING (outbox)
1. `IOutboxProcessorStore` — five methods replaced by `ClaimBatchAsync` + `ApplyOutcomesAsync`;
   three moved to `IOutboxAdminStore` / `IOutboxRetentionStore`.
2. `IOutboxProcessor.ProcessBatchAsync` — returns `ValueTask<OutboxBatchResult>` (ADR-MSG-015).
3. `IOutboxCoordinator.ExecuteAsync` — returns `ValueTask<OutboxBatchResult>` (ADR-MSG-015).
4. `OutboxProcessor` and `EfOutboxStore<TContext>` — new `TimeProvider` (and, for the processor,
   `Random`) constructor dependencies.
5. `OutboxMessage` — new `ClaimToken` property; requires
   `ALTER TABLE ... ADD COLUMN claim_token uuid NULL` for consumers who own their DDL.
6. `OutboxProcessorOptions` — `init` accessors become `set`.
7. **`BatchSize` 20 → 100 and `MaxRetries` 10 → 5 are behavioural changes, not merely new
   defaults.** Both bind unchanged from existing configuration but halve the retry budget.
8. `MediatROutboxDispatcher` and `InProcessIntegrationDispatcher` throw `OutboxPayloadException`
   instead of `InvalidOperationException` on an unresolvable or malformed payload.

### Added — integration event publishing

The first half of the integration-event path: a notification handler states that a domain fact
becomes a published contract, and the row is staged durably in the handler's own transaction.
Nothing delivers it yet — the relay, the envelope and the broker adapters are the transport lot.

See ADR-MSG-018.

- **`IIntegrationEventPublisher`** — `PublishAsync<TEvent>(evt, occurredOnUtc, ct)` returns the
  assigned `MessageId`. It **stages and never commits**: if the caller's transaction rolls back,
  the event was never published. Same contract `IOutboxWriter.AddAsync` has one transaction
  earlier.
- **`[IntegrationEvent("name.v1")]`** — mandatory on every published event. The CLR type name
  cannot serve as a wire identity: a consumer in another service holds a different type in a
  different assembly, and a namespace rename would invalidate rows already in flight.
- **`IntegrationEventMessage`** and its own table. Metadata lives in columns so a claim can filter
  and order without parsing JSON. Two timestamps, deliberately: `CreatedAtUtc` (staging, always
  set, what a claim orders on) and `OccurredOnUtc` (the business fact, optional). Ordering on the
  business timestamp would let a backdated event jump the queue.
- **Per-module registration.** `AddIntegrationEventContracts(source, …)` once per module,
  `AddIntegrationEventPublishing()` once per application, `AddEfCoreIntegrationEvents<TContext>()`
  for the staging writer. The `source` is stored per registration, not in a shared singleton that
  the last module to register would overwrite for all the others. Composing every module into one
  registry is also what makes a cross-module contract-name collision detectable at all.
- **Startup validation.** A duplicated contract name fails at boot, not on the one code path that
  emits that event — where it would roll back a transaction and be retried forever as a transient
  failure.

**A handler must publish inside a transaction it opened, and `CommitAsync` alone is not that.**
`IUnitOfWork.CommitAsync` is a bare `SaveChangesAsync` running under the provider's implicit
per-call transaction, which never appears as an open one. Publishing therefore belongs inside
`ITransactionalContext.ExecuteAsync`; `IntegrationEventPublishException` says so at the point of
failure, and `IIntegrationEventPublisher` carries a worked example.

### Fixed

- **`IExecutionContext` reached constructor injection for the first time.** The per-message
  execution scope exposed the message-row context by wrapping the scope's `IServiceProvider`, and
  that wrapper is consulted only for a direct `GetService` call — Microsoft DI activates
  constructor dependencies from its own scope and never sees it. Every service taking
  `IExecutionContext` as a constructor parameter therefore received the default registration: a
  fresh `CorrelationId` with a null `TenantId`. Cascade outbox rows written by
  `OutboxDomainEventSink` lost their tenant and had their correlation chain severed at exactly the
  hop this module exists to preserve. `IExecutionContext` now resolves through a scoped
  `ExecutionContextHolder` that the scope factory populates, so constructor injection sees the
  message row. The `PassThroughExecutionScopeFactory` docs, which described the old behaviour as a
  permanent defect, are corrected.

### Changed — BREAKING (in-process transport withdrawn)

- **`IMessagePublisher` and `InProcessMessagePublisher` are deleted.** The fan-out they performed —
  one `InboxMessage` per registered consumer — moves into `InProcessIntegrationDispatcher`, with
  **every field taken from the `OutboxMessage` row** rather than from the event instance.
  Behaviour is unchanged; the metadata source is not.

  This closes a latent bug. The inbox deduplicates on `(MessageId, ConsumerType)`, and that
  `MessageId` must be stable across redeliveries of the same outbox row. Reading it off the
  deserialized event survived that only by accident: the same payload happens to deserialize to
  the same value, but nothing guaranteed it — not the contract, not the serializer, and not an
  event type free to compute its identity in a property initializer.

  **Migration:** none for consumers who compose with `AddInProcessTransport()`, which keeps its
  `IMessageSerializer` and `IOutboxDispatcher` registrations. A consumer who implemented
  `IMessagePublisher` themselves has no replacement in this release; the transport seam arrives
  with the transport libraries.

- **`IIntegrationEvent` loses every member and becomes a bare marker**, keeping its `IEvent` base.
  `MessageId`, `TenantId`, `CorrelationId`, `CausationId` and `OccurredOnUtc` are gone. They
  existed only because `IMessagePublisher` had no other metadata source, and every one of them
  also existed as a column on the message row with nothing keeping the two in agreement — a
  discrepancy nothing detects, on the field that governs isolation.

  **Migration:** delete those members from your event types. They are inert the moment the
  interface stops declaring them, so the compiler will point at each one (CA1822 on any that were
  expression-bodied). Metadata is assigned at staging from the ambient `IExecutionContext`.

  Both packages are `1.0.0-preview.*` with no external consumers, so both breaks ship outright
  rather than accumulating a permanent shim. After `1.0.0` neither is available at any reasonable
  cost.

### Migration — integration event schema

One new table. Nothing existing changes, so this is additive and needs no downtime.

Consumers who call `modelBuilder.ApplyMessagingConfiguration()` and generate their own EF
migrations need no manual step — the configuration is applied automatically. The DDL below is for
consumers who own their schema, and is the script EF Core emits for this model on PostgreSQL,
reproduced verbatim rather than transcribed.

```sql
CREATE TABLE "IntegrationEventMessages" (
    "Id" uuid NOT NULL,
    "ContractName" character varying(256) NOT NULL,
    "Source" character varying(256) NOT NULL,
    "Data" text NOT NULL,
    "TenantId" character varying(256),
    "CorrelationId" uuid,
    "CausationId" uuid,
    "TraceParent" character varying(64),

    -- Two timestamps, deliberately. CreatedAtUtc is when the row was staged and is always set;
    -- OccurredOnUtc is when the fact happened and is null when the caller did not say. A claim
    -- orders on the first — ordering on the second would let a backdated event jump the queue
    -- and make queue order depend on caller-supplied data.
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "OccurredOnUtc" timestamp with time zone,

    -- Status answers "where is this now"; DeadLettered answers "was it ever given up on".
    -- Orthogonal on purpose: a single Failed status would lose the second answer the moment a
    -- requeue moved the row back to pending. A dead-lettered row reads Pending + flag set.
    "Status" character varying(32) NOT NULL,
    "DeadLettered" boolean NOT NULL,

    "RetryCount" integer NOT NULL,
    "NextRetryAtUtc" timestamp with time zone,
    "LockedUntilUtc" timestamp with time zone,
    "ClaimToken" uuid,
    "ProcessedAtUtc" timestamp with time zone,
    "ErrorMessage" character varying(2048),
    CONSTRAINT "PK_IntegrationEventMessages" PRIMARY KEY ("Id")
);

-- The claim path: not dead-lettered, pending or past its back-off deadline, oldest staged first.
CREATE INDEX "IX_IntegrationEventMessages_Dispatchable"
    ON "IntegrationEventMessages" ("DeadLettered", "Status", "NextRetryAtUtc", "CreatedAtUtc");

-- Read-back by token, and lease recovery after a crashed relay.
CREATE INDEX "IX_IntegrationEventMessages_ClaimToken"
    ON "IntegrationEventMessages" ("ClaimToken");

-- Retention sweeps delivered rows by age.
CREATE INDEX "IX_IntegrationEventMessages_TenantId_ProcessedAt"
    ON "IntegrationEventMessages" ("TenantId", "ProcessedAtUtc");
```

> The EF Core configuration ships **unfiltered** indexes because `HasFilter` takes
> provider-specific SQL and `MicroKit.Messaging.EntityFrameworkCore` is the provider-neutral
> package. A consumer writing their own DDL should prefer the partial forms
> (`WHERE "ClaimToken" IS NOT NULL`, `WHERE "DeadLettered" = false`).

> ⚠ Identifier casing differs from the inbox migration published above, and the divergence is
> pre-existing rather than introduced here: the shipped EF configuration emits quoted PascalCase
> identifiers, while that earlier SQL block is snake_case. This block matches the code. See
> `L0-FINDINGS.md` #24.

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
