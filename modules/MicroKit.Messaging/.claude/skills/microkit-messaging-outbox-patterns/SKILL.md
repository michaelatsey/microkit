# Skill: microkit-messaging-outbox-patterns

Implementation guide for the transactional outbox/inbox pattern in MicroKit.Messaging.

> **Rules govern constraints. Skills illustrate implementation. Where they conflict, rules win.**
> Canonical contracts: `microkit-messaging-outbox-inbox.md`
>
> ⚠ This file was rewritten after the outbox claim rewrite (#87) and the inbox claim rewrite (#90).
> The per-message lease API it used to teach — `GetPendingAsync`, `AcquireLeaseAsync`,
> `MarkPublishedAsync`, `MarkFailedAsync`, `DeadLetterAsync`, `MarkProcessingAsync`,
> `MarkProcessedAsync` — **no longer exists on any interface**. If you find a snippet using it
> anywhere, that snippet is stale.

---

## The shape, in one diagram

```
OutboxWorker : BackgroundService        internal sealed — when: loop + adaptive cadence
  └─► IOutboxCoordinator                public  — where: which database(s)
        SharedDbOutboxCoordinator       internal sealed, v1 default
      └─► IOutboxProcessor              public  — what: one batch
            OutboxProcessor             internal sealed
              ├─► IOutboxProcessorStore   ClaimBatchAsync / ApplyOutcomesAsync
              ├─► IExecutionScopeFactory  one scope per message
              └─► IOutboxDispatcher       deserialize + deliver (resolved per message)
```

The inbox is the same four roles: `InboxWorker` → `IInboxCoordinator` → `IInboxProcessor` →
`IInboxProcessorStore` / `IInboxSettlementStore`.

**`OutboxProcessor` is not a `BackgroundService`.** `OutboxWorker` is, and it is `internal`.
Do not register either by hand — `AddMicroKitMessaging()` does it.

---

## Step 1 — stage the row in the caller's transaction

`IOutboxWriter` **stages only**. It never calls `SaveChangesAsync`; the caller's unit of work owns
the boundary, which is what makes the write atomic with the aggregate.

```csharp
// ✅ The domain-event path — what you get from AddMediatRDomainEvents().
//    You write no outbox code at all. OutboxDomainEventSink does it:
//    drain → map each event to its DomainEventNotification → one AddBatchAsync.
public sealed class PlaceOrderHandler(IOrderRepository repo)
    : ICommandHandler<PlaceOrderCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(PlaceOrderCommand cmd, CancellationToken ct = default)
    {
        var order = Order.Place(cmd.CustomerId, cmd.Lines);
        order.Raise(new OrderPlacedEvent(order.Id));   // ← the only line that matters
        await repo.AddAsync(order, ct);
        return Result.Success(order.Id);
    }
    // No SaveChanges, no publish. TransactionBehavior (order 700) dispatches then commits.
}
```

```csharp
// ✅ The direct path — writing an integration event yourself.
var message = outboxMessageFactory.Create(          // OutboxMessageFactory, singleton
    payload:       evt,                              // any object; runtime type is stamped
    messageId:     evt.MessageId.Value,              // intrinsic to the event, never regenerated
    occurredOnUtc: evt.OccurredOnUtc,                // intrinsic to the event
    context:       executionContext);                // ambient: TenantId/Correlation/Causation

await outboxWriter.AddAsync(message, ct);            // stages; does NOT save
// ... your unit of work commits, and the row commits with it.
```

```csharp
// ❌ WRONG — publish before commit. The broker has it; the commit may still fail.
await publisher.PublishAsync(evt, ct);
await uow.CommitAsync(ct);
```

> **What the outbox actually carries.** `OutboxMessage.EventType`/`Payload` hold whatever was
> serialized — on the MediatR path a `DomainEventNotification<TEvent>`, which is **not** an
> `IIntegrationEvent`. Never wire a publisher onto `IOutboxWriter` assuming integration events.
> `IOutboxDispatcher` is the seam that decides what a payload is.

---

## Step 2 — the claim: one atomic `UPDATE`, one ownership token

The per-message lease is gone. Round trips per batch went from `2N+1` to two (three when
contended), plus one settlement.

```csharp
// EfOutboxStore.ClaimBatchAsync — the three steps, and why each is separate.

// 1. Candidates. A separate query, NOT OrderBy/Take inside ExecuteUpdate: whether row-limiting
//    operators translate there varies by EF Core version and provider.
//    Full rows, not ids — that is what lets step 3 be skipped when nothing was contended.
var candidates = await Dispatchable(now)
    .OrderBy(m => m.OccurredOnUtc)
    .Take(batchSize)
    .ToListAsync(ct);

var candidateIds = candidates.ConvertAll(m => m.Id);
candidateIds.Sort();   // deterministic lock order — two processors with intersecting candidate
                       // sets must not lock in opposite orders. Needs MessageId : IComparable<T>.

// 2. Stamp — the atomic part. The eligibility predicate is REPLAYED inside the UPDATE, so a row
//    another processor claimed between step 1 and step 2 simply does not match.
var claimedCount = await context.Set<OutboxMessage>()
    .Where(m => candidateIds.Contains(m.Id))
    .Where(EligibilityPredicate(now))          // ← replayed, not assumed
    .ExecuteUpdateAsync(s => s
        .SetProperty(m => m.Status, OutboxMessageStatus.Processing)
        .SetProperty(m => m.LockedUntilUtc, lockExpiry)
        .SetProperty(m => m.ClaimToken, token), ct);

// 3. Read back ONLY when contended. Winning every candidate means the rows in hand ARE the claim.
if (claimedCount == candidates.Count) { /* patch in memory, return */ }
```

```csharp
// The eligibility predicate. The stale-lease arm is mandatory: without it a crashed processor's
// messages are stuck until someone intervenes.
m => !m.DeadLettered
     && (m.Status == OutboxMessageStatus.Pending
         || (m.Status == OutboxMessageStatus.Processing && m.LockedUntilUtc <= now))
     && (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= now);
```

```csharp
// ❌ WRONG — SELECT + mutate + SaveChanges is NOT atomic under concurrent processors.
var messages = await ctx.OutboxMessages.Where(m => m.Status == Pending).ToListAsync();
foreach (var m in messages) m.Status = Processing;
await ctx.SaveChangesAsync();   // two processors can both SELECT the same rows first
```

> **Why a token and not `FOR UPDATE SKIP LOCKED`.** This is the provider-neutral EF Core package;
> a PostgreSQL locking clause would leak a provider dependency and leave the production claim path
> untestable under SQLite. Under `READ COMMITTED` a blocked `UPDATE` re-evaluates its `WHERE`
> against the committed row version, so the loser is rejected correctly. Proven by
> `PostgreSql/OutboxClaimConcurrencyTests`, not assumed.

> **The inbox claim is the same shape with one difference that is not optional.** It selects
> candidates by the **single-column `RowId`** surrogate. Filtering an `UPDATE` with two `Contains`
> over the compound `(MessageId, ConsumerType)` key selects the CROSS PRODUCT of both lists — it
> claims rows nobody chose and can exceed `batchSize` several times over. And `ClaimToken` is
> mapped as an **EF concurrency token** on the inbox; without it `StageProcessedAsync` is
> decorative. Pinned by `ClaimBatchAsync_NeverExceedsBatchSize` and `PostgreSql/InboxLeaseExpiryTests`.

---

## Step 3 — dispatch, one scope per message

```csharp
// ✅ OutboxProcessor's loop, in essence.
foreach (var message in claim.Messages)
{
    try
    {
        // Scope creation is INSIDE the try on purpose: a tenant-aware factory may do I/O to
        // resolve a per-tenant connection, and that failure is a dispatch failure like any
        // other. Outside, it would abort the batch without settling a single outcome.
        await using var scope = await _executionScopeFactory.CreateScopeAsync(ctx, ct);
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
        await dispatcher.DispatchAsync(message, ct);

        outcomes.Add(OutboxOutcome.Published(message.Id));
    }
    catch (OutboxPayloadException ex)   { /* dead-letter on FIRST sight */ }
    catch (OutboxTransportUnavailableException) { /* abort batch, release remainder */ }
    catch (Exception ex)                { outcomes.Add(BuildTransientOutcome(message, ex)); }
}

// ❌ Shared scope across the batch — a DbContext fault on message N corrupts message N+1.
await using var shared = _scopeFactory.CreateAsyncScope();
foreach (var message in batch) await ProcessSingleAsync(shared, message, ct);
```

### Failure classification — the table that decides whether a message survives

| Exception | Response |
|---|---|
| `OutboxPayloadException` | dead-letter **on first sight** — unresolvable `EventType`, malformed JSON, payload matching no known contract |
| `OutboxTransportUnavailableException` | abort the batch, release the remainder, **consume no retries** |
| `OutboxConfigurationException` | settle the batch (all released), then rethrow so the worker stops |
| anything else | transient — retry with back-off until `MaxRetries` |

> ⚠ **Never throw `OutboxPayloadException` for** a broker nack, a timeout, a refused connection,
> an HTTP 503 or a database timeout. Only proven permanence dead-letters. A library that guesses
> permanence wrongly loses messages.

---

## Step 4 — settlement: four outcomes, one write, every statement token-filtered

```csharp
await _store.ApplyOutcomesAsync(claim.Token, outcomes, settleCts.Token);
```

| Kind | Persisted | Retry budget |
|---|---|---|
| `Published` | `Status=Published`, `ProcessedAtUtc`, lease + token cleared | — |
| `Retry` | `Status=Pending`, `RetryCount++`, `NextRetryAtUtc`, error text | consumed |
| `DeadLetter` | `Status=Failed`, `DeadLettered=true`, `ProcessedAtUtc`, error text | terminal |
| `Released` | `Status=Pending`, lease + token cleared, **nothing else** | **not consumed** |

`Released` is what stops a broker outage from burning the retry allowance of the whole queue.
Every claimed message carries exactly one outcome — `OutboxProcessor` asserts it — or leases are
stranded for the whole `LockDuration`.

```csharp
// ✅ Every terminal write filters on the token. This is not an optimisation; it is the fix for a
//    lost update — a processor whose lease expired could otherwise overwrite the processor that
//    legitimately re-claimed its message.
.Where(m => ids.Contains(m.Id) && m.ClaimToken == claimToken)

// ✅ Settlement runs on a SHORT INDEPENDENT timeout, deliberately not the caller's shutdown
//    token: cancelling this write strands every lease in the batch.
using var settleCts = new CancellationTokenSource(_options.OutcomeFlushTimeout);

// ✅ A row count below outcomes.Count is REPORTED, never assumed to be success.
if (written < outcomes.Count) OutboxProcessorLogs.PartialSettlement(_logger, written, outcomes.Count);
```

> **Known defect — settlement is not transactional with the dispatch target.** The `Published`
> mark is written after the whole batch, in its own transaction. Where consumers sit behind the
> inbox that costs duplication only; where `MediatROutboxDispatcher` runs `INotificationHandler`s
> in-process there is no inbox row, so a replay re-runs them and re-writes their rows. L0 finding
> #23. Notification handlers on this path **must** be idempotent.

---

## Retry back-off — exponential ceiling, then full jitter

Computed by the processor, not the store: back-off is retry policy, not persistence, and it must
be unit-testable without a database. `TimeProvider` and `Random` are both injected, which is what
lets the curve be asserted against exact values.

```csharp
// NextRetryAtUtc = now + Uniform(0, min(2^retryCount seconds, MaxRetryBackoff))

internal TimeSpan ComputeBackoffCeiling(int retryCount)
{
    var exponent = Math.Clamp(retryCount, 0, 20);   // a corrupt count cannot overflow
    var backoff = TimeSpan.FromSeconds(1L << exponent);
    return backoff > _options.MaxRetryBackoff ? _options.MaxRetryBackoff : backoff;
}

internal TimeSpan ApplyJitter(TimeSpan ceiling) =>
    ceiling <= TimeSpan.Zero                        // ← the guard is not optional
        ? TimeSpan.Zero
        : TimeSpan.FromTicks((long)(ceiling.Ticks * _random.NextDouble()));

// Ceiling: 0→1s, 1→2s, 2→4s, 5→32s, 10→1024s, 12+→3600s (default cap).
// Actual delay: a uniform draw anywhere in [0, ceiling].
```

```csharp
// ❌ Deterministic back-off — the messages that fail together (which is what an outage produces)
//    all retry at the same instant across every processor instance, forever.
TimeSpan delay = TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, retryCount)));
```

**The inbox uses the same formula, jitter included.** It is not the older deterministic one.

---

## Adaptive cadence

`OutboxWorker.NextDelay` derives the interval from the batch result instead of a fixed timer:

| Result | Next interval |
|---|---|
| `AbortReason == TransportUnavailable` | grow toward `TransportUnavailableBackoff` (5 min) |
| `AbortReason == Cancelled` | unchanged — the loop is exiting |
| `IsSaturated(BatchSize)` | `TimeSpan.Zero` — poll again immediately |
| `!HasWork` | grow geometrically toward `MaxPollingInterval` (1 min) |
| partial batch | back to `PollingInterval` (5 s) |

---

## Inbox ingestion — the dedup gate

```csharp
// ✅ Call AddAsync UNCONDITIONALLY and read the result. The unique index on
//    (MessageId, ConsumerType) is the sole authority.
var result = await inboxWriter.AddAsync(message, ct);
metrics.Record(result, message.ConsumerType);

if (result is InboxWriteResult.AlreadyPresent)
{
    InboxIngestionLogs.Deduplicated(logger, message.MessageId.Value, message.ConsumerType);
    continue;   // ← next consumer. NOT a dispatch failure, and NOT an early return.
}
```

The `continue` is load-bearing twice. The publisher returns normally, so the outbox marks the
message `Published` instead of retrying it to death — **and** consumers after a duplicated one
still get their row. When the duplicate escaped as an exception it ended the whole publish, so a
partial redelivery became permanent loss for consumers 3..N.

```csharp
// ❌ FORBIDDEN — guarding the insert with ExistsAsync is a time-of-check-to-time-of-use race.
if (!await store.ExistsAsync(messageId, consumerType, ct))
    await store.AddAsync(message, ct);   // two ingesters both see false, both insert, one throws
```

`ExistsAsync` exists for operator tooling and for the store's own **post-hoc verification** — it
asks "is the row recorded *now*?" *after* a failed insert, which is a question with no race in it.
That is how `EfInboxStore` recognises a duplicate: it never decodes a provider error code.

> **A redelivery is not an error.** Reported through the return value, logged at `Debug`, counted
> as `microkit.inbox.messages.deduplicated`. The **rate** is the signal.

---

## Inbox drain — success settles inside the handler's transaction

This is where the inbox stops being a mirror of the outbox.

```csharp
// ✅ InboxProcessor.HandleAsync, in essence.
await using var scope = await _executionScopeFactory.CreateScopeAsync(ctx, ct);

var handler    = scope.ServiceProvider.GetRequiredService(entry.HandlerType);
var settlement = scope.ServiceProvider.GetRequiredService<IInboxSettlementStore>();

// Staged BEFORE invoking, so the mark is part of whatever unit of work the handler commits.
var owned = await settlement.StageProcessedAsync(key, claimToken, ct);
if (!owned) return HandleResult.LeaseLost;   // another processor owns it — do NOT invoke

await entry.Invoker(handler, evt, ct);       // handler's SaveChanges commits mark + side effects

if (settlement.IsMarkUncommitted(key))       // handler did no DB work — fall back, and warn
    return HandleResult.SucceededWithoutCommit;
```

Order matters in the catch chain: **lease-lost is checked before post-commit**, because a lost
lease leaves the staged entry `Modified`, so `IsMarkUncommitted` is true and the post-commit branch
would miss it — consuming a retry for a row this processor no longer owns.

| Kind | Effect on the row | Effect on the batch |
|---|---|---|
| `InboxPayloadException` (unknown consumer, unreadable payload) | dead-letter **immediately** | continues |
| anything unrecognised | retry, full-jitter back-off | continues |
| `InboxDependencyUnavailableException` | **released**, no retry spent | abandoned |
| `InboxConfigurationException` | released, no retry spent | abandoned, **worker stops** |
| lease lost | untouched, owned elsewhere | continues |

> **Guarantee, stated plainly:** with a handler that writes through the scope's `DbContext`, inbox
> processing is **transactionally atomic** — mark and side effects commit together or not at all.
> It is NOT exactly-once in general: a handler that calls an external endpoint and then rolls back
> calls it again on replay. A handler that commits no unit of work at all is **detected and
> reported**, not degraded silently.

---

## Retention

Two workers, two windows, and harmonising them would be a defect.

| | Default | Deleting early loses |
|---|---|---|
| `OutboxRetentionWorker` | `RetentionDays = 7` | history |
| `InboxRetentionWorker` | `RetentionDays = 30` | **the deduplication guarantee** |

The inbox only deduplicates messages it still holds, so its window must exceed the maximum
plausible redelivery delay of every upstream transport. Both delete across every tenant
(`tenantId: null`), which is also the only value that reaches rows in a single-tenant deployment
where `TenantId` is itself null. `RetentionDays <= 0` disables retention: the worker logs once and
returns.

---

## EF Core configuration — what is load-bearing

Both configurations ship in `MicroKit.Messaging.EntityFrameworkCore`; apply them with
`modelBuilder.ApplyMessagingConfiguration()`. Do not hand-roll them. Two lines are correctness,
not style:

```csharp
// InboxMessageConfiguration — DECISION 1: single-column surrogate primary key.
// The compound key stays the dedup gate through the unique index; the surrogate is what makes a
// batch claim expressible as ONE bounded list instead of a cross product.
builder.HasKey(m => m.RowId);
builder.Property(m => m.RowId).ValueGeneratedNever();

builder.HasIndex(m => new { m.MessageId, m.ConsumerType })
    .IsUnique()
    .HasDatabaseName("UX_InboxMessages_MessageId_ConsumerType");

// InboxMessageConfiguration — DECISION 2: ClaimToken is a concurrency token.
// Without it the UPDATE that SaveChanges emits carries only the primary key, ownership is checked
// at read time only, and the lost update returns. Removing this line breaks no test that does not
// exercise concurrency.
builder.Property(m => m.ClaimToken).IsConcurrencyToken();
```

`TenantId` is **optional on both tables** (no `IsRequired()`) — Messaging must run without
Multitenancy (ADR-EXEC-001). Neither table carries a global query filter: they are infrastructure
tables read cross-tenant by the processors (ADR-MSG-002).

Indexes are deliberately **unfiltered**. A partial index (`WHERE dead_lettered = false`) would stay
smaller as processed rows accumulate, but `HasFilter` takes provider-specific SQL and this is the
provider-neutral package. A consumer writing their own DDL should prefer the partial form.
