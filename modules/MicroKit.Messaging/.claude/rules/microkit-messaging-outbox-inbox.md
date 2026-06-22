# microkit-messaging-outbox-inbox

## Always active for any task touching OutboxProcessor, InboxProcessor, IOutboxWriter, IOutboxProcessorStore, IInboxStore, OutboxMessage, or InboxMessage.

---

## Outbox Pattern — Guarantee

The transactional outbox guarantees **at-least-once delivery**:
1. Integration event written to `outbox_messages` in the **same database transaction** as the domain commit
2. Background processor (`OutboxProcessor`) polls and dispatches pending messages
3. Message marked `Published` only after broker/handler confirms delivery

**If step 1 fails, the business transaction fails — no orphaned messages.**
**If step 3 never happens, the message is retried — no silent loss.**

---

## OutboxMessage Shape

`OutboxMessage` is a **`sealed class`** — not a `sealed record`. EF Core must mutate
`Status`, `LockedUntilUtc`, `RetryCount`, and other fields as the message moves through
its state machine. `init`-only record properties cannot be assigned by EF Core change tracking.

```csharp
public sealed class OutboxMessage
{
    public MessageId Id { get; set; } = null!;
    public string TenantId { get; set; } = null!;              // REQUIRED — never null
    public string EventType { get; set; } = null!;             // fully qualified CLR type name
    public string Payload { get; set; } = null!;               // JSON-serialized event
    public OutboxMessageStatus Status { get; set; }            // see state machine below
    public int RetryCount { get; set; }                        // incremented on each failed attempt
    public DateTimeOffset OccurredOnUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }        // set when Published or DeadLettered
    public DateTimeOffset? LockedUntilUtc { get; set; }        // lease expiry — null when not locked
    public DateTimeOffset? NextRetryAtUtc { get; set; }        // earliest eligible retry time
    public string? ErrorMessage { get; set; }                  // last error message
    public bool DeadLettered { get; set; }                     // true = terminal, max retries exceeded
    public CorrelationId CorrelationId { get; set; } = null!;
    public CausationId? CausationId { get; set; }              // nullable — root events have no cause
}
```

---

## Outbox State Machine

```
Pending
  └── OutboxProcessor.AcquireLeaseAsync (atomic UPDATE WHERE Status=Pending)
       └── Processing
            ├── broker/handler confirms → Published  (terminal — ProcessedAtUtc set)
            └── exception thrown
                 ├── RetryCount + 1 < MaxRetries → Pending (RetryCount++, NextRetryAtUtc = now + BackOff)
                 └── RetryCount + 1 >= MaxRetries → Failed + DeadLettered=true (terminal)
```

### Status values

| Status | Meaning |
|--------|---------|
| `Pending` | Written, not dispatched. Eligible when `NextRetryAtUtc IS NULL OR <= now`. |
| `Processing` | Lease held — in-flight. `LockedUntilUtc > now`. |
| `Published` | Confirmed delivery. Terminal — no further transitions. |
| `Failed` | `RetryCount >= MaxRetries` AND `DeadLettered=true`. Terminal — no retries. |

> **`Processing` is never the final failure state.** A failed attempt resets to `Pending`
> (with back-off). `Failed` always means permanently dead — `DeadLettered=true` is always
> set simultaneously. There is no transient `Failed` state.

### State transition rules
- `Pending → Processing`: atomic via `AcquireLeaseAsync` — single `UPDATE WHERE Status='Pending'`
- `Processing → Published`: only after confirmed delivery
- `Processing → Pending (retry)`: `RetryCount++`, `LockedUntilUtc = null`, `NextRetryAtUtc = now + BackOff(retryCount)`
- `Processing → Failed + DeadLettered=true`: when `RetryCount + 1 >= MaxRetries`
- `Published` and `Failed/DeadLettered=true` are terminal

---

## Lease / Lock Pattern

```csharp
// ✅ Two-step: GetPendingAsync (candidates read) + AcquireLeaseAsync (atomic lock)
//
// GetPendingAsync is a READ-ONLY query — it does NOT acquire a lease.
// AcquireLeaseAsync executes a single atomic UPDATE WHERE to prevent double-processing:
//
//   UPDATE outbox_messages
//   SET Status = 'Processing', LockedUntilUtc = @expiry
//   WHERE Id = @id
//     AND (Status = 'Pending' OR (Status = 'Processing' AND LockedUntilUtc <= @callTime))
//     AND (NextRetryAtUtc IS NULL OR NextRetryAtUtc <= @callTime)
//
// Returns true if 1 row was updated (lease acquired), false if 0 rows (another processor won).
//
// ⚠ Stale-lease recovery: the OR clause on Status = 'Processing' AND LockedUntilUtc <= @callTime
// is mandatory. GetPendingAsync re-surfaces stale Processing messages; without this OR,
// AcquireLeaseAsync would always return false on those rows, leaving crashed-processor messages
// permanently stuck. @callTime = DateTimeOffset.UtcNow captured once before the call.
//
// ⚠ EF Core LINQ (SELECT + foreach mutate + SaveChanges) is NOT atomic under concurrent
// processors — do not use it for lease acquisition. Use ExecuteUpdateAsync or raw SQL.

// ✅ Lease duration configurable
public sealed record OutboxProcessorOptions
{
    public int LockDurationInMinutes { get; init; } = 5;
    public int BatchSize { get; init; } = 20;
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxRetries { get; init; } = 10;
    public int RetentionDays { get; init; } = 7;        // Published rows kept this long before cleanup
}
```

### Stale lock recovery

Messages with `LockedUntilUtc < now` AND `Status = Processing` are re-eligible:
`GetPendingAsync` includes `(LockedUntilUtc IS NULL OR LockedUntilUtc < now)` in its WHERE clause.
Crashed processors release their lease automatically when the lock expires.

---

## Retry Back-Off Formula

```csharp
// ✅ Exponential back-off with 1-hour cap
public static TimeSpan CalculateDelay(int retryCount)
    => TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, retryCount)));

// retryCount=0 → 1s, 1→2s, 2→4s, 3→8s, 5→32s, 10→1024s, 11+→3600s (cap)

// ❌ Linear retry — causes thundering herd under load
TimeSpan delay = TimeSpan.FromSeconds(retryCount * 30);
```

---

## InboxMessage Shape

`InboxMessage` is also a **`sealed class`** for the same EF Core mutation reasons.

```csharp
public sealed class InboxMessage
{
    public MessageId MessageId { get; set; } = null!;        // dedup key — part 1
    public string ConsumerType { get; set; } = null!;         // fully qualified handler type name — dedup key part 2
    public string TenantId { get; set; } = null!;             // REQUIRED — never null
    public string EventType { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public InboxMessageStatus Status { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }       // lease expiry for concurrent processors
    public string? ErrorMessage { get; set; }
    public CorrelationId? CorrelationId { get; set; }          // nullable — inbound messages from external systems may lack correlation context
    public CausationId? CausationId { get; set; }             // nullable — root events have no cause
}
```

> **Nullability asymmetry:** `OutboxMessage.CorrelationId` is non-nullable (`= null!`) because
> outbound messages must always be traceable — set to `CorrelationId.New()` if no upstream context.
> `InboxMessage.CorrelationId` is nullable because inbound messages arrive from external systems
> that may not carry correlation context. Do NOT "fix" this asymmetry — it is intentional.

---

## Inbox Dedup Pattern (Idempotency Gate)

```csharp
// ✅ Compound unique key = (MessageId + ConsumerType)
// One envelope can be consumed by multiple handlers independently — each gets its own row.

// ✅ AUTHORITATIVE GUARD: compound PK unique constraint (not ExistsAsync)
// ExistsAsync is a fast-path read optimization. Under concurrent load, two processors
// can both pass ExistsAsync (both see false), then race on AddAsync.
// The compound PK constraint is the real gate — only one AddAsync succeeds.

// ✅ Handler invocation flow
async ValueTask ProcessAsync<T>(
    MessageEnvelope<T> envelope,
    IMessageHandler<T> handler,
    CancellationToken ct) where T : IIntegrationEvent
{
    // consumerType comes from the handler's type — NOT from any inbox row
    var consumerType = typeof(handler).FullName!;

    // 1. Fast-path read (optimization, not the sole guard)
    if (await _inboxStore.ExistsAsync(envelope.MessageId, consumerType, ct).ConfigureAwait(false))
        return;

    // 2. Attempt to record receipt — compound PK is the real concurrency guard
    try
    {
        await _inboxStore.AddAsync(InboxMessage.FromEnvelope(envelope, consumerType), ct).ConfigureAwait(false);
    }
    catch (DbUpdateException)
    {
        return; // unique constraint violation — another processor won, skip
    }

    // 3. Acquire lease — lockUntil derived from InboxProcessorOptions.LeaseDuration
    var lockUntil = DateTimeOffset.UtcNow.Add(_options.LeaseDuration);
    await _inboxStore.MarkProcessingAsync(envelope.MessageId, consumerType, lockUntil, ct).ConfigureAwait(false);

    // 4. Invoke handler
    await handler.HandleAsync(envelope.Event, ct).ConfigureAwait(false);

    // 5. Mark Processed
    await _inboxStore.MarkProcessedAsync(envelope.MessageId, consumerType, ct).ConfigureAwait(false);
}

// ❌ Check after processing — idempotency failure
await handler.HandleAsync(payload, ct);
if (await _inboxStore.ExistsAsync(...)) { ... } // too late — duplicate already processed
```

---

## IOutboxWriter Contract (Abstractions — domain handlers only)

```csharp
/// <summary>
/// Write-only outbox access for domain handlers.
/// Resolved from the same DbContext as the domain aggregate to guarantee atomicity.
/// </summary>
public interface IOutboxWriter
{
    /// <summary>
    /// Adds an outbox message in the current domain transaction.
    /// Throws on database error — the exception propagates through the unit of work.
    /// </summary>
    ValueTask AddAsync(OutboxMessage message, CancellationToken ct = default);

    /// <summary>
    /// Adds multiple outbox messages in one EF Core AddRange call (ADR-MSG-011).
    /// Used by DomainEventsDispatcher P4 for single-round-trip batch writes.
    /// An empty list is a no-op.
    /// </summary>
    ValueTask AddBatchAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default);
}
```

---

## IOutboxProcessorStore Contract (Abstractions — background processor only)

```csharp
/// <summary>
/// Outbox access for OutboxProcessor. Never injected into domain handlers.
/// </summary>
public interface IOutboxProcessorStore
{
    /// <summary>
    /// Returns pending message candidates (read-only — does not acquire a lease).
    /// Filter: Status=Pending AND (LockedUntilUtc IS NULL OR LockedUntilUtc &lt; now)
    ///         AND (NextRetryAtUtc IS NULL OR NextRetryAtUtc &lt;= now).
    /// Processes ALL tenants — TenantId is read from each OutboxMessage row, not passed as a filter.
    /// </summary>
    ValueTask<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize, CancellationToken ct = default);

    /// <summary>
    /// Atomically acquires a lease on a single message using a single UPDATE WHERE.
    /// Matches both fresh Pending messages and stale Processing messages whose
    /// LockedUntilUtc has expired (crashed-processor recovery).
    /// Returns true if acquired (1 row updated), false if another processor won (0 rows).
    /// </summary>
    ValueTask<bool> AcquireLeaseAsync(
        MessageId id, DateTimeOffset lockExpiry, CancellationToken ct = default);

    /// <summary>Marks a message as successfully published. Terminal.</summary>
    ValueTask<Result> MarkPublishedAsync(MessageId id, CancellationToken ct = default);

    /// <summary>
    /// Resets a message to Pending after a transient failure.
    /// Increments RetryCount, clears LockedUntilUtc, sets NextRetryAtUtc = now + BackOff(retryCount).
    /// </summary>
    ValueTask<Result> MarkFailedAsync(
        MessageId id, string errorMessage, int retryCount, CancellationToken ct = default);

    /// <summary>
    /// Marks a message as dead-lettered when RetryCount >= MaxRetries.
    /// Sets Status=Failed, DeadLettered=true, ProcessedAtUtc=now. Terminal.
    /// </summary>
    ValueTask<Result> DeadLetterAsync(MessageId id, string reason, CancellationToken ct = default);

    /// <summary>
    /// Deletes Published messages older than olderThan. Used by the cleanup worker.
    /// Returns the count of deleted rows.
    /// </summary>
    ValueTask<int> DeleteProcessedAsync(
        DateTimeOffset olderThan, string tenantId, CancellationToken ct = default);

    /// <summary>Returns dead-lettered messages for operator inspection.</summary>
    ValueTask<IReadOnlyList<OutboxMessage>> GetDeadLetteredAsync(
        int batchSize, string tenantId, CancellationToken ct = default);

    /// <summary>Re-queues a dead-lettered message (resets to Pending, DeadLettered=false).</summary>
    ValueTask<Result> RequeueAsync(MessageId id, CancellationToken ct = default);
}
```

> The EF Core implementation (`EfOutboxStore`) implements **both** `IOutboxWriter` and
> `IOutboxProcessorStore`. They are separate interfaces to enforce ISP:
> domain handlers never accidentally call `GetPendingAsync` or `DeadLetterAsync`.

---

## IInboxStore Contract

```csharp
public interface IInboxStore
{
    /// <summary>
    /// Fast-path dedup check. Read optimization — not the sole concurrency guard.
    /// The compound PK (MessageId, ConsumerType) is the authoritative guard.
    /// </summary>
    ValueTask<bool> ExistsAsync(
        MessageId messageId, string consumerType, CancellationToken ct = default);

    /// <summary>
    /// Records receipt of an inbound message.
    /// Throws DbUpdateException on duplicate (unique constraint on compound PK).
    /// </summary>
    ValueTask AddAsync(InboxMessage message, CancellationToken ct = default);

    /// <summary>
    /// Returns pending inbox messages.
    /// Processes ALL tenants — TenantId is read from each InboxMessage row, not passed as a filter.
    /// </summary>
    ValueTask<IReadOnlyList<InboxMessage>> GetPendingAsync(
        int batchSize, CancellationToken ct = default);

    /// <summary>
    /// Acquires a processing lease. Returns plain ValueTask — throws on failure
    /// (e.g. database error). This is a non-optional precondition: a failure here
    /// means the inbox row is in an inconsistent state and must not be silently swallowed.
    /// </summary>
    ValueTask MarkProcessingAsync(
        MessageId messageId, string consumerType, DateTimeOffset lockUntil,
        CancellationToken ct = default);

    /// <summary>Marks message as successfully processed.</summary>
    ValueTask<Result> MarkProcessedAsync(
        MessageId messageId, string consumerType, CancellationToken ct = default);

    /// <summary>Marks message as failed and increments retry count.</summary>
    ValueTask<Result> MarkFailedAsync(
        MessageId messageId, string consumerType, string errorMessage, CancellationToken ct = default);
}
```

---

## Batch Processing Conventions

```csharp
// ✅ One scope per message — failure in one does not affect others
foreach (var message in batch)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var processorStore = scope.ServiceProvider.GetRequiredService<IOutboxProcessorStore>();
    var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();

    await ProcessSingleAsync(processorStore, publisher, message, ct).ConfigureAwait(false);
}

// ❌ Shared scope across batch — DbContext state from message N bleeds into message N+1
await using var shared = _scopeFactory.CreateAsyncScope();
foreach (var message in batch)
{
    await ProcessSingleAsync(shared, message, ct); // ← isolation bug
}
```

> **One scope per message is mandatory.** A shared scope means a DbContext exception on
> message N will corrupt the DbContext state for message N+1. Per-message scopes also
> ensure that `TenantId` context is fresh for each message.

---

## Silent Success Prohibition

```csharp
// ❌ FORBIDDEN — fake success when publisher is null
public async ValueTask PublishAsync<T>(T evt, CancellationToken ct = default) where T : IIntegrationEvent
{
    if (_innerPublisher is null) return; // ← message lost with no error
}

// ✅ REQUIRED — throw on null publisher
public async ValueTask PublishAsync<T>(T evt, CancellationToken ct = default) where T : IIntegrationEvent
{
    if (_innerPublisher is null)
        throw new InvalidOperationException(
            "No IMessagePublisher registered. Call AddInProcessTransport() or a broker provider.");

    await _innerPublisher.PublishAsync(evt, ct).ConfigureAwait(false);
}
```
