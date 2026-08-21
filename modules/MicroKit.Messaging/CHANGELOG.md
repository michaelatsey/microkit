# Changelog — MicroKit.Messaging

## [Unreleased] — outbox claim rewrite

Replaces the per-message lease with an atomic batch claim carrying an ownership token, buffers
dispositions in memory, and settles them in one transactional write. Round trips per batch drop
from `2N+1` to two (three when contended) plus one settlement — flat in `N`.

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

### Added
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

### Changed — BREAKING
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
