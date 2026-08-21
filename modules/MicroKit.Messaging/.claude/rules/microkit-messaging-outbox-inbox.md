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
    public string? TenantId { get; set; }                      // optional — null in single-tenant (ADR-MSG-008 §5)
    public string EventType { get; set; } = null!;             // fully qualified CLR type name
    public string Payload { get; set; } = null!;               // JSON-serialized event
    public OutboxMessageStatus Status { get; set; }            // see state machine below
    public int RetryCount { get; set; }                        // incremented on each failed attempt
    public DateTimeOffset OccurredOnUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }        // set when Published or DeadLettered
    public DateTimeOffset? LockedUntilUtc { get; set; }        // lease expiry — null when not locked
    public Guid? ClaimToken { get; set; }                      // ownership proof — null when not leased
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
  └── ClaimBatchAsync (atomic UPDATE WHERE, stamps Status/LockedUntilUtc/ClaimToken)
       └── Processing
            ├── dispatch confirms          → Published  (terminal — ProcessedAtUtc set)
            ├── OutboxPayloadException     → Failed + DeadLettered=true (terminal, FIRST attempt)
            ├── batch aborted / cancelled  → Pending    (Released — NO retry consumed)
            └── other exception
                 ├── RetryCount + 1 < MaxRetries  → Pending (RetryCount++, NextRetryAtUtc = now + BackOff)
                 └── RetryCount + 1 >= MaxRetries → Failed + DeadLettered=true (terminal)
```

> Every transition out of `Processing` is buffered as an `OutboxOutcome` and written by ONE
> `ApplyOutcomesAsync` call per batch, each statement filtered on the claim token.

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
- `Pending → Processing`: atomic via `ClaimBatchAsync` — one `UPDATE WHERE` over the candidate ids,
  replaying the eligibility predicate, stamping `ClaimToken`
- `Processing → Published`: only after confirmed dispatch; clears lease and token
- `Processing → Pending (retry)`: `RetryCount++`, lease and token cleared,
  `NextRetryAtUtc = now + Uniform(0, min(2^RetryCount s, MaxRetryBackoff))`
- `Processing → Pending (released)`: lease and token cleared, **nothing else** — no retry consumed
- `Processing → Failed + DeadLettered=true`: when `RetryCount + 1 >= MaxRetries`, or immediately on
  `OutboxPayloadException`
- `Published` and `Failed/DeadLettered=true` are terminal
- Every one of the above filters on `ClaimToken`: a lost lease writes zero rows

---

## Claim / Settlement Pattern

The per-message lease is gone. One atomic claim reserves a whole batch and stamps it with an
ownership token; one settlement writes every disposition back. Round trips per batch went from
`2N+1` to two (three when contended) plus one settlement.

```csharp
// ✅ Step 1 — candidates. A separate query, NOT OrderBy/Take inside ExecuteUpdate.
//    Full rows, not ids: that is what lets step 3 be skipped when nothing was contended.
var candidates = await Dispatchable(now).OrderBy(m => m.OccurredOnUtc).Take(batchSize).ToListAsync(ct);

var candidateIds = candidates.ConvertAll(m => m.Id);
candidateIds.Sort();   // deterministic lock order — two processors with intersecting candidate
                       // sets must not lock in opposite orders. Requires MessageId : IComparable<T>.

// ✅ Step 2 — the atomic part. The eligibility predicate is REPLAYED inside the UPDATE, so a row
//    another processor claimed between step 1 and step 2 simply does not match.
//
//   UPDATE outbox_messages
//   SET Status = 'Processing', LockedUntilUtc = @expiry, ClaimToken = @token
//   WHERE Id = ANY(@candidateIds)
//     AND NOT DeadLettered
//     AND (Status = 'Pending' OR (Status = 'Processing' AND LockedUntilUtc <= @now))
//     AND (NextRetryAtUtc IS NULL OR NextRetryAtUtc <= @now)
//
//   Under READ COMMITTED a blocked UPDATE re-evaluates its WHERE against the committed row
//   version once the lock is released, so the loser is rejected correctly. That is the mechanism
//   the token claim rests on instead of FOR UPDATE SKIP LOCKED — proven by the PostgreSQL
//   concurrency suite, not assumed.

// ✅ Step 3 — read back ONLY when contended. Winning every candidate means the rows already in
//    hand ARE the claim.
if (claimedCount == candidates.Count) { /* patch in memory, return */ }
```

> ⚠ **Stale-lease recovery** — the `Status = 'Processing' AND LockedUntilUtc <= @now` arm is
> mandatory. Without it, a crashed processor's messages are stuck until someone intervenes.

> ⚠ **Every terminal write filters on `ClaimToken`.** This is not an optimization; it is the fix
> for a lost update. Filtering on the id alone let a processor whose lease had expired overwrite
> the processor that legitimately re-claimed its message. Zero rows written is now a *reportable
> outcome* — `ApplyOutcomesAsync` returns the affected-row count and the processor logs a partial
> settlement — rather than an unconditional `Result.Success()`.

> ⚠ **EF Core LINQ (SELECT + foreach mutate + SaveChanges) is NOT atomic** under concurrent
> processors. Use `ExecuteUpdateAsync`.

### Options

```csharp
// ✅ The shipped shape. Note { get; set; }, not { get; init; }: with init-only accessors the
//    Action<OutboxProcessorOptions> callback taken by AddMicroKitMessaging could not assign
//    anything, so every outbox configuration callback was silently a no-op.
public sealed record OutboxProcessorOptions
{
    public int BatchSize { get; set; } = 100;                                    // was 20
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);     // base cadence
    public TimeSpan MaxPollingInterval { get; set; } = TimeSpan.FromMinutes(1);  // idle ceiling
    public TimeSpan TransportUnavailableBackoff { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxRetries { get; set; } = 5;                                     // was 10
    public TimeSpan MaxRetryBackoff { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan OutcomeFlushTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxErrorMessageLength { get; set; } = 2000;
    public int RetentionDays { get; set; } = 7;          // 0 or less disables retention entirely
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(1);
}

// ⚠ BatchSize 20→100 and MaxRetries 10→5 are BEHAVIOURAL changes, not merely new properties:
//   both bind unchanged from existing configuration but halve the retry budget.
```

### Adaptive cadence

`OutboxWorker` derives its next interval from the batch result rather than sleeping on a fixed
timer: saturated batch → poll again immediately; idle queue → grow geometrically toward
`MaxPollingInterval`; transport outage → grow toward `TransportUnavailableBackoff`; partial batch →
back to `PollingInterval`.

### Retention

`OutboxRetentionWorker` deletes `Published` rows older than `RetentionDays` on a slow timer
(`RetentionInterval`), across every tenant (`tenantId: null`). Before it existed,
`DeleteProcessedAsync` had no caller and `RetentionDays` was read by nothing.



## Retry Back-Off Formula

Computed by `OutboxProcessor`, not by the store: back-off is retry policy, not persistence, and it
must be unit-testable without a database.

```csharp
// ✅ Exponential ceiling, then FULL JITTER over the whole interval.
//    NextRetryAtUtc = now + Uniform(0, min(2^retryCount seconds, MaxRetryBackoff))

internal TimeSpan ComputeBackoffCeiling(int retryCount)
{
    var exponent = Math.Clamp(retryCount, 0, 20);          // a corrupt count cannot overflow
    var backoff = TimeSpan.FromSeconds(1L << exponent);
    return backoff > _options.MaxRetryBackoff ? _options.MaxRetryBackoff : backoff;
}

internal TimeSpan ApplyJitter(TimeSpan ceiling) =>
    ceiling <= TimeSpan.Zero
        ? TimeSpan.Zero
        : TimeSpan.FromTicks((long)(ceiling.Ticks * _random.NextDouble()));

// Ceiling: retryCount=0 → 1s, 1→2s, 2→4s, 3→8s, 5→32s, 10→1024s, 12+→3600s (default cap).
// Actual delay: a uniform draw anywhere in [0, ceiling].

// ❌ Deterministic back-off — with several processor instances, the messages that fail together
//    (which is exactly what a broker outage produces) all retry at the same instant, and keep
//    doing so on every subsequent attempt.
TimeSpan delay = TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, retryCount)));

// ❌ Linear retry — causes thundering herd under load
TimeSpan delay = TimeSpan.FromSeconds(retryCount * 30);
```

> **The clock and the jitter source are both injected** (`TimeProvider`, `Random`), which is what
> lets the exponential curve be asserted against exact values with the jitter neutralised, rather
> than approximated with a tolerance. Production registers `TimeProvider.System` and `Random.Shared`.
> The inbox retains the older deterministic formula until the inbox lot.

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

## Outbox Store Contracts (Abstractions — split by consumer, ISP)

One class may implement all three; three interfaces so a DLQ console never sees `ClaimBatchAsync`
and the processor never sees `RequeueAsync`.

```csharp
/// <summary>Claim and settlement. Background processor only.</summary>
public interface IOutboxProcessorStore
{
    /// <summary>
    /// Atomically reserves up to batchSize dispatchable messages and stamps them with a lease
    /// and an ownership token. Two concurrent processors must never both win the same row.
    /// Dispatchable = pending, or processing with an expired lease (crash recovery), not
    /// dead-lettered, and past NextRetryAtUtc.
    /// </summary>
    ValueTask<OutboxClaim> ClaimBatchAsync(
        int batchSize, TimeSpan lockDuration, CancellationToken ct = default);

    /// <summary>
    /// Persists the disposition of every message from the matching claim, in one round trip.
    /// EVERY write filters on claimToken. Returns the affected-row count: a value below
    /// outcomes.Count means leases were lost mid-batch, which the caller logs rather than
    /// assuming success.
    /// The token is a SHORT INDEPENDENT timeout, deliberately NOT the caller's shutdown token —
    /// cancelling this write strands every lease in the batch.
    /// </summary>
    ValueTask<int> ApplyOutcomesAsync(
        Guid claimToken, IReadOnlyList<OutboxOutcome> outcomes, CancellationToken ct = default);
}

/// <summary>Dead-letter inspection and requeueing. Operator tooling only.</summary>
public interface IOutboxAdminStore
{
    // tenantId null = every tenant, and the ONLY value that reaches rows in a single-tenant
    // deployment where TenantId is itself null. The old non-nullable signature matched nothing.
    ValueTask<IReadOnlyList<OutboxMessage>> GetDeadLetteredAsync(
        int batchSize, string? tenantId = null, CancellationToken ct = default);

    /// <summary>Returns true only if a row was actually requeued — never unconditional success.</summary>
    ValueTask<bool> RequeueAsync(MessageId id, CancellationToken ct = default);
}

/// <summary>Retention. The cleanup worker only.</summary>
public interface IOutboxRetentionStore
{
    ValueTask<int> DeleteProcessedAsync(
        DateTimeOffset olderThan, string? tenantId = null, CancellationToken ct = default);
}
```

### Outcome model

`OutboxOutcome` is a `readonly record struct` buffered in memory during the batch and applied in one
write. Four kinds, and the difference between the last two is load-bearing:

| Kind | Persisted | Retry budget |
|---|---|---|
| `Published` | `Status=Published`, `ProcessedAtUtc`, lease + token cleared | — |
| `Retry` | `Status=Pending`, `RetryCount++`, `NextRetryAtUtc`, error text | consumed |
| `DeadLetter` | `Status=Failed`, `DeadLettered=true`, `ProcessedAtUtc`, error text | terminal |
| `Released` | `Status=Pending`, lease + token cleared, nothing else | **not consumed** |

> `Released` is what stops a broker outage from burning the retry allowance of every message in the
> queue. A message claimed but never attempted — because the batch aborted on
> `OutboxTransportUnavailableException`, a cancellation, or a missing registration — goes back
> untouched.

### Dispatcher failure classification

`IOutboxDispatcher` implementations signal permanence with typed exceptions:

| Exception | Processor response |
|---|---|
| `OutboxPayloadException` | dead-letter **on first sight** — unresolvable `EventType`, malformed JSON, payload matching no known contract |
| `OutboxTransportUnavailableException` | abort the batch, release the remainder, consume no retries |
| `OutboxConfigurationException` | settle the batch (all released), then rethrow so the worker stops |
| anything else | transient — retry with back-off until `MaxRetries` |

> ⚠ **Never throw `OutboxPayloadException` for** a broker nack, a timeout, a refused connection, an
> HTTP 503 or a database timeout. Only proven permanence dead-letters; everything unrecognised stays
> transient. A library that guesses permanence wrongly loses messages.


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
