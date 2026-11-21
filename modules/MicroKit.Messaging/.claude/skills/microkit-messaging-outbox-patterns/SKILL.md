# Skill: microkit-messaging-outbox-patterns

Implementation guide for the outbox, the inbox guard and the store engine in MicroKit.Messaging,
and for writing a store, a transport or an integration package against the SPI.

> **Rules govern constraints. Skills illustrate implementation. Where they conflict, rules win.**
> Canonical contracts: `.claude/rules/microkit-messaging-outbox-inbox.md`. The decision: ADR-MSG-020.

---

## The shape

```
AddMicroKitMessaging(bus => ...)
  bus.UseEntityFrameworkOutbox<TContext>()  ─► AddMessageStore(EntityFrameworkOutbox.KeyOf<TContext>(), ...)
                                               (first store also registers MessageStoreEngine)

MessageStoreEngine : BackgroundService        reads IMessageStoreSet once; per store:
  ├─ OutboxLoop          ─► IOutboxProcessor (keyed by store)  ─► IOutboxProcessorStore (keyed)
  │                           one scope per row ─► IOutboxDispatcher keyed by OutboxDispatchers.KeyOf(row)
  │                                                 ├─ Endpoint null    → TransportOutboxDispatcher → IMessageTransport
  │                                                 └─ Endpoint "local" → LocalOutboxDispatcher → MessageReceivePipeline
  ├─ InboxLoop           ─► IInboxProcessor  (keyed by store)  ─► IInboxProcessorStore (keyed)
  │                           each claimed row ─► MessageReceivePipeline.RetryAsync
  ├─ OutboxRetentionLoop ─► IOutboxRetentionStore (keyed)
  └─ InboxRetentionLoop  ─► IInboxRetentionStore  (keyed)
```

Everything under `Processing/` is `internal`. Nothing is registered by hand: the first
`AddMessageStore` registers the engine, the keyed processors and **both** dispatchers
(`StoreEngineRegistration.AddStore`), so a transport row leaves through whatever `IMessageTransport`
the host registers — the in-memory one or a provider's. Each loop creates a scope per cycle and resolves its store's
services from it, so a store whose database is down backs off alone.

---

## Writing a store provider

A store is the outbox **and** the inbox of one unit of work. Register it through the SPI only —
`UseEntityFrameworkOutbox` is built on it alone:

```csharp
public static MessagingBuilder UseMyStore(this MessagingBuilder bus)
{
    var key = new MessageStoreKey("mystore:orders");
    return bus.AddMessageStore(key, store =>
    {
        // Scoped, keyed by the store key, ONE instance per scope behind every interface: the
        // guard must share the unit of work the handler resolves from the same scope.
        store.Services.TryAddScoped<MyStore>();
        store.Services.TryAddKeyedScoped<IOutboxWriter>(key, static (sp, _) => sp.GetRequiredService<MyStore>());
        store.Services.TryAddKeyedScoped<IOutboxProcessorStore>(key, static (sp, _) => sp.GetRequiredService<MyStore>());
        store.Services.TryAddKeyedScoped<IOutboxRetentionStore>(key, static (sp, _) => sp.GetRequiredService<MyStore>());
        store.Services.TryAddKeyedScoped<IInboxGuardStore>(key, static (sp, _) => sp.GetRequiredService<MyStore>());
        store.Services.TryAddKeyedScoped<IInboxProcessorStore>(key, static (sp, _) => sp.GetRequiredService<MyStore>());
        store.Services.TryAddKeyedScoped<IInboxRetentionStore>(key, static (sp, _) => sp.GetRequiredService<MyStore>());
        // + IOutboxAdminStore / IInboxAdminStore for operator tooling

        store.AddStartupCheck(sp => /* each problem as a message naming its fix */ []);
    });
}
```

- The six services in `StoreEngineRegistration.RequiredStoreServices` are required: host start
  rejects a store missing one.
- A start check reports problems; it never throws one of them. The EF store checks that `TContext`
  is registered and that its model carries both entities.
- With several stores, `store.BindAssembly(assembly)` (EF: `ForAssemblyContaining<TMarker>()`)
  decides which store a type of that assembly publishes to and which inbox its handlers use.

---

## Step 1 — publishing is enlisting

`IOutboxWriter.AddAsync` adds the row to the scope's unit of work and **never saves**. The caller's
`SaveChanges` commits it with the business data; inside a delivery, the engine's commit does.

```csharp
// EfOutboxStore — the writer.
public ValueTask AddAsync(OutboxMessage message, CancellationToken ct = default)
{
    context.Set<OutboxMessage>().Add(message);
    (_enlisted ??= []).Add(message);   // checked when the scope ends
    return ValueTask.CompletedTask;
}

// EfOutboxStore.Dispose — an enlisted row is never lost silently.
//   still Added    → event 4000 (Error): the scope was never saved
//   Detached       → event 4001 (Debug): discarded with the change tracker
```

```csharp
// ✅ Application code: publish from any scoped service. No transaction is required.
await bus.PublishAsync(new OrderPlaced(order.Id), ct: ct);
await unitOfWork.CommitAsync(ct);      // commits the aggregate AND the outbox row

// ❌ A broker SDK called directly before commit: the broker has it, the commit may still fail.
await brokerClient.PublishAsync(evt, ct);
await unitOfWork.CommitAsync(ct);
```

The bus builds the row with `OutboxMessageFactory.CreateContract` (`Endpoint` null, `ContractName`
= wire type, `Source` = service name, the publication's own headers). An integration package sends a
local message through `ILocalMessageSender`, which enlists a `CreateLocal` row (`Endpoint =
"local"`, no contract) the same way — or, with no store, queues it in memory.

---

## Step 2 — the claim: one atomic `UPDATE`, one ownership token

```csharp
// EfOutboxStore.ClaimBatchAsync, in essence.

// 1. Candidates: a separate query, NOT OrderBy/Take inside ExecuteUpdate (translation varies by
//    provider and version). Full untracked rows, so step 3 can be skipped when uncontended.
var candidates = await Dispatchable(now)
    .OrderBy(m => m.CreatedAtUtc)              // staging time — never OccurredOnUtc, which callers supply
    .Take(batchSize)
    .ToListAsync(ct);

var candidateIds = candidates.ConvertAll(m => m.Id);
candidateIds.Sort();                           // one lock order: no deadlock between intersecting claims

// 2. Stamp — the atomic part. The eligibility predicate is REPLAYED inside the UPDATE, so a row
//    another loop claimed between step 1 and step 2 does not match.
var claimedCount = await context.Set<OutboxMessage>()
    .Where(m => candidateIds.Contains(m.Id))
    .Where(EligibilityPredicate(now))
    .ExecuteUpdateAsync(s => s
        .SetProperty(m => m.Status, OutboxMessageStatus.Processing)
        .SetProperty(m => m.LockedUntilUtc, lockExpiry)
        .SetProperty(m => m.ClaimToken, token), ct);

// 3. Read back by token only when some candidates were lost; otherwise patch the rows in memory.
```

```csharp
// The eligibility predicate. The expired-lease arm is crash recovery.
m => !m.DeadLettered
     && (m.Status == OutboxMessageStatus.Pending
         || (m.Status == OutboxMessageStatus.Processing && m.LockedUntilUtc <= now))
     && (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= now);
```

```csharp
// ❌ SELECT + mutate + SaveChanges is not atomic: two loops SELECT the same rows first.
var rows = await ctx.OutboxMessages.Where(m => m.Status == Pending).ToListAsync();
foreach (var m in rows) m.Status = Processing;
await ctx.SaveChangesAsync();
```

> **Why a token and not `FOR UPDATE SKIP LOCKED`.** The EF package is provider-neutral, and a
> locking clause would leave the production claim untestable on SQLite. Under `READ COMMITTED` a
> blocked `UPDATE` re-evaluates its `WHERE` against the committed row, so the loser does not match.
> Proven by `PostgreSql/OutboxClaimConcurrencyTests`, not assumed.

**The inbox claim** has the same shape, with two differences that are not optional:

- Candidates are selected by the single-column **`RowId`**, ordered on `ReceivedAtUtc`. Two
  `Contains` over the compound (`MessageId`, `ConsumerType`) key select the cross product and break
  the `batchSize` bound (`ClaimBatchAsync_NeverExceedsBatchSize`,
  `PostgreSql/InboxClaimConcurrencyTests`).
- Claimable = not dead-lettered and `Failed` or `Received`, or `Processing` with an expired lease,
  with `NextRetryAtUtc` reached. The read-back always runs and fills the key → `RowId` map the
  settlement uses, so settle on the instance that claimed.

---

## Step 3 — dispatch, one scope per row

```csharp
// OutboxProcessor, in essence.
foreach (var message in claim.Messages)
{
    try
    {
        // Inside the try: a tenant-aware scope factory may do I/O, and its failure is this row's.
        await using var scope = await _executionScopeFactory.CreateScopeAsync(
            new ExecutionContext { TenantId = message.TenantId,
                                   CorrelationId = message.CorrelationId?.Value.ToString(),  // copied
                                   CausationId = message.Id.Value.ToString() },              // derived
            ct);
        holder.Store = _storeKey;                 // DeliveryStoreHolder: a publication here enlists in this store

        var dispatcher = ResolveDispatcher(scope.ServiceProvider, message);   // keyed by row Endpoint
        await dispatcher.DispatchAsync(message, ct);
        outcomes.Add(OutboxOutcome.Published(message.Id));
    }
    catch (Exception ex) when (HostStop.Is(ex, ct))       { /* release the rest */ break; }
    catch (OutboxTransportUnavailableException)           { /* defer this endpoint (its back-off); go on */ }
    catch (OutboxConfigurationException)                  { /* release, settle, rethrow */ break; }
    catch (OutboxDispatcherActivationException)           { /* defer this endpoint (1009); go on */ }
    catch (OutboxPayloadException ex)                     { outcomes.Add(OutboxOutcome.DeadLetter(...)); }
    catch (Exception ex)                                  { outcomes.Add(BuildTransientOutcome(message, ex)); }
}
// every row from the break onward: OutboxOutcome.Released
```

```csharp
// ResolveDispatcher — null and throwing are different verdicts.
var key = OutboxDispatchers.KeyOf(message)                 // null → Transport, "local" → Local
    ?? throw new OutboxPayloadException(...);              // any other endpoint: dead-letter
try { dispatcher = serviceProvider.GetKeyedService<IOutboxDispatcher>(key); }
catch (InvalidOperationException ex) when (ex is not ObjectDisposedException)
{ throw new OutboxDispatcherActivationException(ex); }     // registered, cannot be built
return dispatcher ?? throw new OutboxConfigurationException(...);   // not registered
```

| Raised | Response |
|---|---|
| `OutboxPayloadException` | dead-letter **on first sight**: unroutable endpoint, a transport row without `ContractName` or `Source`, a local payload no process can read |
| `OutboxTransportUnavailableException` | defer the endpoint: that row and its endpoint's later rows settled `Deferred` — `NextRetryAtUtc` at the end of the endpoint's back-off, `RetryCount` and error text untouched (`OutboxEndpointBackoff`: `PollingInterval` doubling to `TransportUnavailableBackoff`, cleared by a sent row); **no retry consumed**; the other endpoint's rows are still dispatched |
| `OutboxConfigurationException` (no dispatcher registered for the key — a container composed outside `AddMessageStore`) | release, settle, rethrow: that store's outbox loop stops (1402) |
| activation `InvalidOperationException` — e.g. a transport row with no `IMessageTransport` registered, which the transport dispatcher takes in its constructor | defer the endpoint as above, **no retry consumed**, no rethrow: `DispatcherActivationFailed`, event 1009 naming the call to add; the local rows are still delivered, and the transport is tried once per back-off period until one is deployed. Known defect #124: tenant-scoped, it holds the endpoint's rows of every tenant |
| host stop | release the rest, nothing counted |
| anything else | transient: retry with back-off until `MaxRetries` |

> ⚠ **Never throw `OutboxPayloadException`** for a broker nack, a timeout, a refused connection, an
> HTTP 503 or a database timeout. Only proven permanence dead-letters.

### Writing a transport

- Register the `IMessageTransport` and nothing else from the outbox side: a store already registers
  the internal transport dispatcher that feeds it
  (`MessagingBuilderTransportTests.AStoreWithAProvidersOwnTransport_SendsItsTransportRowsThroughIt`).
- `SendAsync` returns **after** the broker acknowledged (publisher confirm, `SendMessagesAsync`,
  delivery report). The outbox marks the row `Published` on that return, and `Published` is terminal.
- Classify by exception type: `OutboxTransportUnavailableException` when the next message would fail
  too; `OutboxPayloadException` when the destination rejects this content permanently; anything else
  is a per-message retry.
- The envelope arrives built: `Id`, `Source`, `Type` (the wire type), `Time`, `DataContentType`
  (`application/json`), the row's headers plus `tenantid`, `correlationid`, `causationid`,
  `traceparent`, and `Body` — the payload bytes, never deserialized on the way out.
- The receiving half: hand each arriving message to `IEnvelopeReceiver.ReceiveAsync` and settle the
  broker message only after it completed, by `ReceiveDisposition`. `Unsettled` settles nothing.
- A conformance test is owed for each half: `SendAsync` does not return before the broker
  acknowledged; nothing is settled at the broker while `ReceiveAsync` is outstanding.

---

## Step 4 — settlement: one call, every statement token-filtered

```csharp
using var settleCts = new CancellationTokenSource(_options.OutcomeFlushTimeout);   // never the host token
var written = await _store.ApplyOutcomesAsync(claim.Token, outcomes, settleCts.Token);
if (written < outcomes.Count) OutboxProcessorLogs.PartialSettlement(_logger, written, outcomes.Count);
// a settlement exception is logged and swallowed: leases expire and the batch is re-claimed
```

| Kind | Outbox row | Retry budget |
|---|---|---|
| `Published` | `Status=Published`, `ProcessedAtUtc`, lease and token cleared | — |
| `Retry` | `Status=Pending`, `RetryCount`, `NextRetryAtUtc`, error text | consumed |
| `DeadLetter` | `Status=Failed`, `DeadLettered=true`, `ProcessedAtUtc`, error text | terminal |
| `Released` | `Status=Pending`, lease and token cleared, nothing else | **not consumed** |
| `Deferred` | `Status=Pending`, `NextRetryAtUtc` = the end of the endpoint's back-off, lease and token cleared; `RetryCount` and error text untouched | **not consumed** |

```csharp
// Every write filters on the token — the fix for the lost update: a loop whose lease expired
// matches zero rows instead of overwriting the loop that re-claimed the row.
.Where(m => ids.Contains(m.Id) && m.ClaimToken == claimToken)
```

- `Published` and `Released` are one set-based statement each, `Deferred` one per distinct next
  attempt; `Retry` and `DeadLetter` one per row.
- `SettlementTransaction.RunAsync` runs the writes as one operation of the context's execution
  strategy, in a transaction of its own (a retrying strategy refuses a user transaction begun
  outside it), joining an ambient transaction when one is open. A commit with an unknown outcome is
  verified by checking that no outcome's row still carries the token.
- On the inbox, `Retry` and `Released` write `Status=Failed` (`Failed` and not dead-lettered *is*
  the awaiting-retry state). A successful retry writes no outcome: its row was marked in the
  handler's transaction.

### Back-off — exponential ceiling, full jitter

```csharp
// NextRetryAtUtc = now + Uniform(0, min(2^n seconds, MaxRetryBackoff))
internal TimeSpan ComputeBackoffCeiling(int retryCount)
{
    var exponent = Math.Clamp(retryCount, 0, 20);   // a corrupt count cannot overflow
    var backoff = TimeSpan.FromSeconds(1L << exponent);
    return backoff > _options.MaxRetryBackoff ? _options.MaxRetryBackoff : backoff;
}

internal TimeSpan ApplyJitter(TimeSpan ceiling) =>
    ceiling <= TimeSpan.Zero ? TimeSpan.Zero
        : TimeSpan.FromTicks((long)(ceiling.Ticks * _random.NextDouble()));
```

The inbox uses the same formula (`InboxRetryPolicy`), for a failed first delivery and for the
retry loop. Deterministic back-off resynchronises every row that failed together — exactly what an
outage produces.

### Adaptive cadence (`OutboxLoop.NextDelay`; `InboxLoop` the same, plus a `DependencyUnavailable` arm growing toward `DependencyUnavailableBackoff`)

| Result | Next delay |
|---|---|
| `Cancelled` | unchanged |
| outbox only: rows handed over in back-off (`HandedOverInBackOff`) | `PollingInterval` |
| saturated (`IsSaturated(BatchSize)`) | `TimeSpan.Zero` — poll again at once |
| no work | grow toward `MaxPollingInterval` (1 min) |
| partial batch | `PollingInterval` (5 s) |
| the cycle threw (1401) | grow toward `MaxPollingInterval` |
| processor could not be activated (1400) | grow toward the outage back-off |

No outage arm on the outbox: a down endpoint's rows are deferred by the processor to the end of the
endpoint's own back-off, and while it runs the claim leaves the endpoint's rows out
(`ClaimBatchAsync(…, OutboxEndpoints excluded, …)`), so the other endpoint keeps this cadence and the
waiting rows are not rewritten. A batch carrying rows handed over in back-off (`HandedOverInBackOff`,
a store that ignores the filter) waits `PollingInterval`, whatever its abort reason and even if it
delivered something.

Grow = double from `PollingInterval`, capped.

---

## The inbox guard — one attempt

A new delivery (`MessageReceivePipeline.DeliverGuardedAsync`) first reads the row
(`IInboxGuardStore.FindAsync`, untracked): a row in **any** state skips the handler. Otherwise it
runs the attempt through the store's execution strategy, each run in a fresh scope:

```csharp
// MessageReceivePipeline.AttemptAsync, in essence.
await using var scope = await OpenScopeAsync(executionContext, store, context, ct);  // initializers run here
var guard = scope.ServiceProvider.GetKeyedService<IInboxGuardStore>(store);

await guard.BeginAsync(ct);                                       // the attempt's transaction
if (claimToken is { } token)                                      // a retry
{
    if (!await guard.MarkClaimedProcessedAsync(key, token, ct))
        return AttemptResult.LeaseLost;                           // do NOT run the handler
}
else                                                              // a new delivery
{
    await guard.EnlistProcessedAsync(rowFactory.Create(InboxMessageStatus.Processed), ct);
}

await RunHandlerAsync(scope.ServiceProvider, entry, message, context, ct);   // middlewares, then handler
await guard.CommitAsync(ct);                                      // row + handler work + publications commit together
```

```csharp
// Run as one operation of the store's strategy; an unknown commit is verified by reading the row.
await guard.ExecuteAsync(
    token => AttemptAsync(attempt, token),
    async token => await guard.FindAsync(attempt.Key, token) is { Status: InboxMessageStatus.Processed },
    AttemptResult.Committed,
    ct);
```

When the attempt throws (and it is not a dependency outage), the row is read again: this attempt's
own `RowId` → it committed then faulted, success; another row → a concurrent delivery won the
unique index, success; none → `RecordFailureAsync` inserts the row `Failed` with
`TryInsertFailedAsync`, in its own scope and transaction, before the delivery is acknowledged.
A permanent failure is written dead-lettered with `RetryCount` 0; a transient one with
`RetryCount` 1 and a back-off (dead-lettered at once when `MaxRetries` is 1); a dependency outage writes nothing and asks the transport to pause
and redeliver.

> **The guard and the handler share one unit of work** because both are resolved from the same
> attempt scope, and the store is registered scoped. Registering it as anything else breaks the
> transaction silently. Pinned by `InboxSettlementGuaranteeTests.TheGuardAndTheHandlerScopeShareOneUnitOfWork`.

> **Exactly-once covers what the handler writes through the store's `DbContext`.** Another
> connection, another `DbContext` or an external call is at-least-once: a replayed attempt reruns
> the handler.

### The retry loop (`InboxProcessor`)

Claims a batch, then for each row: resolve the (`ConsumerType`, message type) pair — bus handlers,
then local consumers; deserialize; rebuild `MessageContext` from the row (`Source`, `Headers`,
tenant, correlation); and call `MessageReceivePipeline.RetryAsync` with the claim token.

| Result | Row | Batch |
|---|---|---|
| processed | marked in the handler's transaction | continues |
| lease lost (2205) | untouched | continues |
| `InboxPayloadException`, `PermanentMessageException`, unknown consumer | `DeadLetter`, `RetryCount` unchanged | continues |
| anything unrecognised | `Retry` with back-off; `DeadLetter` at `MaxRetries` | continues |
| `InboxDependencyUnavailableException` | `Released`, nothing counted | abandoned |
| `InboxConfigurationException` (no guard for the store) | `Released` | abandoned, rethrown: the loop stops |
| host stop | `Released`, nothing counted | abandoned |

`IsLeaseLost` is structural (EF: a `DbUpdateConcurrencyException` on an `InboxMessage` entry); a
business unique violation in a retried handler is the handler's failure and counts an attempt.

---

## Retention

One retention loop per table per store; harmonising the windows would be a defect.

| | Deletes | Default `RetentionDays` | Deleting early loses |
|---|---|---|---|
| `OutboxRetentionLoop` | `Published` rows past the window | 7 | history |
| `InboxRetentionLoop` | `Processed` rows past the window | 30 | **deduplication** |

The inbox only deduplicates what it still holds: its window must exceed the longest plausible
redelivery delay of every upstream transport. Both delete across every tenant (`tenantId: null`),
the only value that reaches rows whose `TenantId` is null. `RetentionDays <= 0` disables the loop:
it logs once and returns.

---

## EF Core configuration — what is load-bearing

Apply both configurations with `modelBuilder.ApplyMessagingConfiguration()`; do not hand-roll them.
Hosts own their migrations.

```csharp
// InboxMessageConfiguration — the surrogate key makes the claim one bounded list.
builder.HasKey(m => m.RowId);
builder.Property(m => m.RowId).ValueGeneratedNever();

// The guard: unique, and the sole authority. One row per (message, handler).
builder.HasIndex(m => new { m.MessageId, m.ConsumerType })
    .IsUnique()
    .HasDatabaseName("UX_InboxMessages_MessageId_ConsumerType");

// ClaimToken is a concurrency token. MarkClaimedProcessedAsync is a tracked update; without this
// line the UPDATE carries the primary key alone and the lost update returns. Removing it breaks no
// test that does not exercise concurrency (PostgreSql/InboxLeaseExpiryTests does).
builder.Property(m => m.ClaimToken).IsConcurrencyToken();
```

- The outbox has **no unique index beside its key** (`TheOutboxTable_HasNoUniqueIndexBesideItsKey`)
  and needs no concurrency token: all its writes are `ExecuteUpdateAsync` with an explicit token
  filter. `Endpoint` is an optional string of at most 64 characters.
- Claim indexes: `IX_OutboxMessages_Dispatchable` (`DeadLettered`, `Status`, `NextRetryAtUtc`,
  `CreatedAtUtc`) and `IX_InboxMessages_Processable` (… `ReceivedAtUtc`). `OccurredOnUtc` is in no
  claim index and no claim sort.
- `TenantId` is optional on both tables, and neither carries a global query filter: they are
  infrastructure tables read across tenants.
- Indexes are unfiltered: `HasFilter` takes provider-specific SQL. A host writing its own DDL may
  prefer partial indexes.
