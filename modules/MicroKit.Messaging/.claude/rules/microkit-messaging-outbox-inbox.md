# microkit-messaging-outbox-inbox

## Always active for any task touching OutboxProcessor, InboxProcessor, IOutboxWriter, IOutboxProcessorStore, IInboxWriter, IInboxProcessorStore, IInboxSettlementStore, OutboxMessage, or InboxMessage.

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
> **The inbox uses the same formula, jitter included** — see `InboxProcessor.ComputeBackoffCeiling`
> and `InboxProcessor.ApplyJitter`. The older deterministic inbox curve is gone (ADR-MSG-017).

---

## InboxMessage Shape

`InboxMessage` is also a **`sealed class`** for the same EF Core mutation reasons.

```csharp
public sealed class InboxMessage
{
    public Guid RowId { get; set; } = Guid.NewGuid();         // PRIMARY KEY — surrogate, writer-assigned
    public MessageId MessageId { get; set; } = null!;         // dedup key — part 1 (unique INDEX, not the PK)
    public string ConsumerType { get; set; } = null!;         // fully qualified handler type name — dedup key part 2
    public string? TenantId { get; set; }                     // optional — null in single-tenant (ADR-MSG-008 §5)
    public string EventType { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public InboxMessageStatus Status { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }       // lease expiry for concurrent processors
    public Guid? ClaimToken { get; set; }                     // ownership proof — EF CONCURRENCY TOKEN
    public DateTimeOffset? NextRetryAtUtc { get; set; }       // earliest eligible retry time
    public bool DeadLettered { get; set; }                    // true = terminal
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
// ✅ Compound dedup key = (MessageId + ConsumerType), enforced by a UNIQUE INDEX.
// One envelope can be consumed by multiple handlers independently — each gets its own row,
// and those rows advance independently: one handler may succeed while another retries.
//
// ✅ The unique index is the SOLE AUTHORITY. ExistsAsync is a fast-path read, never the guard:
// under concurrent load two ingesters can both see false and then race on AddAsync.
//
// ✅ The store recognises the duplicate by POST-HOC VERIFICATION — it asks the database whether
// the row is there NOW, rather than decoding a provider error code. That answers the question
// the decision depends on ("is the message recorded?") instead of the syntactic one a detector
// answers ("was that error a unique violation?"), and it keeps working on providers that do not
// exist yet. It is a check AFTER the failed insert, never a guard before it, so there is no
// time-of-check-to-time-of-use window.

catch (DbUpdateException)
{
    entry.State = EntityState.Detached;   // or the next SaveChanges retries the same insert
    if (savepoint is not null)
        await ambient.RollbackToSavepointAsync(savepoint, ct);   // PostgreSQL aborts the whole txn

    if (await ExistsAsync(message.MessageId, message.ConsumerType, ct))
        return InboxWriteResult.AlreadyPresent;

    throw;   // not the dedup gate — a real fault, and absorbing it would be silent data loss
}
```

> ⚠ **The savepoint is required on every provider, not just PostgreSQL,** and its name is capped
> at **24 characters**: SQL Server rejects savepoint identifiers over 32, so a `Guid:N` with any
> prefix overflows. PostgreSQL tolerates 63, so a Testcontainers-only suite would never catch it.
> Where a provider truncates rather than rejects, two savepoints can share a name and a rollback
> unwinds to the wrong one silently. SQL Server also rejects savepoints inside a distributed
> transaction.

### The publisher side — one line carries the fix

```csharp
var result = await inboxWriter.AddAsync(message, ct);
metrics.Record(result, message.ConsumerType);

if (result is InboxWriteResult.AlreadyPresent)
{
    InboxIngestionLogs.Deduplicated(logger, message.MessageId.Value, message.ConsumerType);
    continue;   // next consumer — NOT a dispatch failure, and NOT an early return
}
```

The `continue` is the repair. The publisher returns normally, so the outbox marks the message
`Published` instead of retrying it to death — **and** consumers after a duplicated one still get
their row. When the duplicate escaped as an exception it ended the whole publish, so a partial
redelivery became permanent loss for consumers 3..N.

```csharp
// ❌ FORBIDDEN — reporting the nominal path as a failure
await _inboxStore.AddAsync(message, ct);   // throws on redelivery; nothing catches it;
                                           // OutboxProcessor calls it transient and dead-letters
                                           // a message that was delivered correctly
```

> **A redelivery is not an error.** Reported through the return value, logged at `Debug`, counted
> as `microkit.inbox.messages.deduplicated`. The **rate** is the signal: a steady low level is
> healthy, a sustained climb means a lease set too short, a stalling consumer, or a broker
> replaying. Note EF Core independently logs the rejected `INSERT` at `Error` — that setting lives
> on the consumer's `DbContext`, so a library cannot silence it.

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
    /// Persists the disposition of every message from the matching claim, in ONE CALL at the
    /// end of the batch — one call, not one statement. The set-based dispositions collapse into
    /// two statements; retries and dead-letters carry per-message values and cost one each,
    /// so the statement count scales with FAILURES, not with batch size. All inside one
    /// transaction.
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


## Inbox Store Contracts (Abstractions — split by consumer, ISP)

One class may implement all five; five interfaces so a publisher never sees `ClaimBatchAsync` and
the drain processor never sees `AddAsync`.

```csharp
/// <summary>Ingestion. Publishers and broker adapters only.</summary>
public interface IInboxWriter
{
    ValueTask<bool> ExistsAsync(MessageId messageId, string consumerType, CancellationToken ct = default);

    /// <summary>
    /// Redelivery is reported through the RETURN VALUE, never through an exception. Under
    /// at-least-once delivery a redelivery needs no failure at all — one expired lease after a
    /// crash is enough — so an exception made the nominal path an error. An exception from this
    /// method means a REAL failure and the caller must treat it as one.
    /// </summary>
    ValueTask<InboxWriteResult> AddAsync(InboxMessage message, CancellationToken ct = default);
}

/// <summary>Claim and deferred settlement. Batch-scoped; the drain processor only.</summary>
public interface IInboxProcessorStore
{
    ValueTask<InboxClaim> ClaimBatchAsync(
        int batchSize, TimeSpan leaseDuration, CancellationToken ct = default);

    /// <summary>
    /// EVERY write filters on claimToken. Returns the affected-row count: a value below
    /// outcomes.Count means those rows no longer carry this batch's token, which the caller logs
    /// rather than assuming success. The token is a SHORT INDEPENDENT timeout, deliberately NOT
    /// the caller's shutdown token — cancelling this write strands every lease in the batch.
    /// </summary>
    ValueTask<int> ApplyOutcomesAsync(
        Guid claimToken, IReadOnlyList<InboxOutcome> outcomes, CancellationToken ct = default);
}

/// <summary>
/// Marks a row processed INSIDE THE HANDLER'S OWN UNIT OF WORK. Resolved from the per-message
/// execution scope, so the mark is staged on the same DbContext the handler wrote through.
/// </summary>
public interface IInboxSettlementStore
{
    /// <summary>Stages only. NEVER calls SaveChangesAsync — the handler's UoW owns the boundary.</summary>
    ValueTask<bool> StageProcessedAsync(
        InboxMessageKey key, Guid claimToken, CancellationToken ct = default);

    /// <summary>True when the handler committed nothing, so the mark needs a deferred write.</summary>
    bool IsMarkUncommitted(InboxMessageKey key);

    /// <summary>
    /// Whether an exception from the handler's commit is this processor's lease being lost.
    /// Structural — which entity failed — never a guess at the message. It lives on the store
    /// because only the implementation knows its provider's exception types, which is what keeps
    /// MicroKit.Messaging free of any EF Core reference.
    /// </summary>
    bool IsLeaseLost(Exception exception);
}

/// <summary>Dead-letter inspection and requeueing. Operator tooling only.</summary>
public interface IInboxAdminStore
{
    ValueTask<IReadOnlyList<InboxMessage>> GetDeadLetteredAsync(
        int batchSize, string? tenantId = null, CancellationToken ct = default);

    /// <summary>Returns true only if a row was actually requeued — never unconditional success.</summary>
    ValueTask<bool> RequeueAsync(InboxMessageKey key, CancellationToken ct = default);
}

/// <summary>Retention. The inbox cleanup worker only.</summary>
public interface IInboxRetentionStore
{
    ValueTask<int> DeleteProcessedAsync(
        DateTimeOffset olderThan, string? tenantId = null, CancellationToken ct = default);
}
```

### Why the inbox is NOT a mirror of the outbox

The outbox settles a whole batch at once. That is tolerable there **only to the extent that its
consumers sit behind this inbox**, where a redelivery costs duplication the unique index absorbs.
For the inbox there is no downstream — the inbox **is** the deduplication. A crash between a
handler returning and its row being marked reruns the handler, with its business side effects.
Batching that settlement would turn one possible replay into N, with nothing underneath to absorb
them.

> ⚠ **Do not read the outbox's batching as safe in general — it is not, and that is a known
> defect, not a property to copy.** Where the outbox dispatches to an in-process handler with no
> inbox row — which is exactly what `MediatROutboxDispatcher` does when it publishes a
> notification through `IPublisher.Publish` — a batch replay re-runs every notification handler
> and re-writes whatever they wrote. The outbox needs the counterpart of
> `IInboxSettlementStore`: a settlement that stages the `Published` mark into the transaction the
> dispatch target commits. Recorded as **L0 finding #23** and on `OutboxProcessor`'s class
> remarks; until it is built, notification handlers on that path **must** be idempotent.

| | Scope | Rationale |
|---|---|---|
| Claim | batch-scoped | Cross-tenant reservation, ADR-MSG-002 preserved |
| Settle success | per-message scope | Joins the handler's transaction — the replay window closes |
| Settle failures and releases | batch-scoped | A failed handler rolled back; there is nothing to join |

`StageProcessedAsync` therefore uses the tracked change pipeline rather than `ExecuteUpdateAsync`,
**precisely because it must not execute immediately**.

> **Guarantee, stated plainly rather than hidden:** with a handler that writes through the scope's
> `DbContext`, inbox processing is **transactionally atomic** — mark and side effects commit
> together or not at all. It is NOT exactly-once in general: a handler that calls an external
> endpoint and then rolls back calls it again on replay. A handler that commits no unit of work at
> all is **detected and reported**, not degraded silently.

### The claim token does two jobs

1. **Fencing** — every write filters on it, so a processor whose lease expired matches zero rows
   instead of overwriting its successor.
2. **Settled marker** — a committed handler transaction leaves it null, so every deferred write for
   that row (including a `Released` issued during shutdown) filters to zero rows and becomes a
   no-op. Nothing has to check for that case; it is structurally impossible.

> ⚠ Job 1 works only because **`ClaimToken` is mapped as an EF concurrency token**
> (`InboxMessageConfiguration`). Without it the `UPDATE` that `SaveChanges` emits carries the
> primary key alone, ownership is checked at read time only, and the lost update returns. Removing
> that line breaks no test that does not exercise concurrency — treat it as part of the contract,
> and note it is pinned by `PostgreSql/InboxLeaseExpiryTests`.

### Inbox failure classification

| Kind | Raised for | Effect on this row | Effect on the batch |
|---|---|---|---|
| **Permanent** (`InboxPayloadException`) | unknown consumer, unreadable payload | dead-letter **immediately** | continues |
| **Transient** (anything unrecognised) | — | retry, full-jitter back-off | continues |
| **Dependency** (`InboxDependencyUnavailableException`) | downstream service unreachable | **released**, no retry spent | abandoned |
| **Configuration** (`InboxConfigurationException`) | handler or settlement store not registered | released, no retry spent | abandoned, **worker stops** |
| **Lease lost** | expired mid-handler | untouched, owned elsewhere | continues |

> ⚠ **Never throw `InboxPayloadException` for** a timeout, a refused connection, an HTTP 503, a
> database timeout or a deadlock. Only proven permanence dead-letters; everything unrecognised
> stays transient. A library that guesses permanence wrongly loses messages.

### Inbox retention — not housekeeping

`InboxRetentionWorker` deletes `Processed` rows older than `RetentionDays` across every tenant
(`tenantId: null`). **`RetentionDays` defaults to 30, not the outbox's 7, and harmonising the two
would be a defect.** On the outbox, deleting early loses history; on the inbox it loses the
deduplication guarantee, because the table only deduplicates messages it still holds. The window
must exceed the maximum plausible redelivery delay of every upstream transport.

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
