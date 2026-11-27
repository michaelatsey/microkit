# microkit-messaging-outbox-inbox

Load before any task touching a store, the store engine (`Processing/`), the receive pipeline
(`Bus/MessageReceivePipeline.cs`), the local endpoint (`Local/`), `OutboxMessage`, `InboxMessage`,
or a store contract in `MicroKit.Messaging.Spi`.

The bus works with no store: a publication goes straight to `IMessageTransport`, a handler runs in a
scope of its own. A **store** — the outbox **and** the inbox of one `DbContext` — makes both durable.
The decision is ADR-MSG-020; the user-facing description is `README.md`.

---

## Guarantees

| Side | Guarantee | Not guaranteed |
|---|---|---|
| Outbox | A publication enlisted in a store commits with the business data, or neither. Once committed, the row reaches its endpoint **at least once**. | Delivery to a handler. A row re-sent after a lost lease or a crash before settlement is a duplicate; the receiving inbox absorbs it. |
| Inbox | One row per (`MessageId`, `ConsumerType`). A handler's work through the store's `DbContext`, its publications, its cascade events and its row commit in one transaction: **exactly once**. | Anything the handler does through another connection or `DbContext`, or an external call: **at least once**, because a replayed attempt reruns the handler. |
| No store | At-least-once per the transport. A redelivered message reruns every handler of it. | Durability. |

Handlers must therefore be idempotent for every effect outside the store's `DbContext`.

---

## Stores

### The SPI

| Type | Role |
|---|---|
| `MessageStoreKey` (application namespace) | Names a store. Non-empty. The EF store's key is `EntityFrameworkOutbox.KeyOf<TContext>()` = `"ef:" + typeof(TContext).FullName`. |
| `AddMessageStore(key, store => ...)` | The one path a store provider registers by. The first call for a key also registers the engine (`StoreEngineRegistration.AddStore`); a second call for the same key configures the same store. |
| `MessageStoreBuilder` | `Key`, `Services`, `BindAssembly(Assembly)`, `AddStartupCheck(...)`. |
| `IMessageStoreSet` | `GetStores()`: the configured stores. Core registers a static set (`StaticMessageStoreSet`), read once when the engine starts and by resolution, binding and the start checks. A partitioned store is in it whether or not it has an instance. |
| `MessageStoreBuilder.PartitionedBy<TSource, TInitializer>()` (#191) | Makes a store partitioned: `TSource` (`IMessageStoreInstanceSource`, a singleton of its own type, forwarded keyed) supplies its `MessageStoreInstance`s — a snapshot, a change token, and `TryGetInstance(MessageContext)` mapping a delivery; `TInitializer` (`IMessageStoreInstanceInitializer`, keyed, scoped) prepares each instance scope. One source per store. |
| `MessageStoreBuilder.AddInstanceCheck(...)` (#191) | A check a partitioned store runs once, in its first instance's prepared scope: problems refuse the store (1409), a throw backs the instance off. The EF model check is one. |

A store registers, **keyed by its `MessageStoreKey` and scoped**: `IOutboxWriter`,
`IOutboxProcessorStore`, `IOutboxRetentionStore`, `IInboxGuardStore`, `IInboxProcessorStore`,
`IInboxRetentionStore` (host start rejects a store missing one —
`StoreEngineRegistration.RequiredStoreServices`), plus `IOutboxAdminStore` and `IInboxAdminStore`
for operator tooling.

> **The store instance and its key always come from the scope**, never from a type fixed at
> registration. The EF store registers `EfOutboxStore<TContext>` and `EfInboxStore<TContext>` once
> each as scoped, and every keyed interface resolves to that instance, so one scope holds one store
> over the scope's `TContext` — the same context the handler resolved from that scope writes through.
> Registering a store as anything but scoped breaks the inbox transaction silently.

### Store resolution — first match wins (`MessageStoreResolver`)

For a publication (`IMessageBus.PublishAsync`) and a local message (`ILocalMessageSender.SendAsync`):

1. `PublishOptions.Store`, when set. It must name a configured store, and inside a delivery it may
   only name that delivery's store; otherwise `IntegrationEventPublishException`. Set with no store
   configured: `IntegrationEventPublishException`.
2. Inside a scope the engine opened for a delivery, that delivery's store (`DeliveryStoreHolder`,
   written by the pipeline and by `OutboxProcessor` on the scope they received).
3. The only store, when there is one.
4. The store the message type's assembly is bound to.
5. Otherwise `IntegrationEventConfigurationException`, naming the stores and the fix. Never a guess,
   never a direct send.

No store configured: `null` — the bus sends to the transport at once.

For a handler (`ForHandler`): no store → unguarded; one store → that one; several → the store its
type's assembly is bound to. Host start rejects, with several stores, a handler or a local consumer
whose assembly is bound to none, and an assembly bound to two.

---

## Outbox

### Publishing is enlisting

With a store, `MessageBus` builds the row with `OutboxMessageFactory.CreateContract` and calls the
store's keyed `IOutboxWriter.AddAsync`. `EfOutboxStore` adds it to the context's change tracker and
**never saves**. No transaction is required: the caller's `SaveChanges` commits the row with the
business data; inside a delivery, the engine's commit does.

- `PublishAsync` resolves `IMessageTransport` first and throws
  `IntegrationEventConfigurationException` without one, store or not. Host start also rejects a
  declared publication with no transport.
- A send middleware that completes without calling the rest of the pipeline is
  `IntegrationEventPublishException`: a publication never succeeds silently.
- When the scope is disposed, `EfOutboxStore.Dispose` reports every row it enlisted: still `Added`
  → event **4000** (`Error`) — lost, the unit of work was never saved; `Detached` → event **4001**
  (`Debug`) — discarded with the change tracker, with the work it announced, or saved and then
  cleared, and published at the next poll if its save committed (no wake, #215). A row still
  `Added` when the scope's guarded inbox attempt never committed is a 4001 too, and goes with that work (`EfInboxAttempt<TContext>`, scoped,
  marked by the inbox store; the inbox store also clears the tracker of such an attempt). Any other
  open transaction — a host's own, through `UseTransaction` — leaves an `Added` row lost (4000). A row saved and then rolled
  back is neither: its business data rolled back with it. Both EF stores implement `IDisposable`
  and `IAsyncDisposable` with the same logic: a scope disposed synchronously reports too.
- `IOutboxWriter.AddBatchAsync` stages several rows in one `AddRange`; an empty list is a no-op.

### `OutboxMessage`

`sealed class` with `{ get; set; }`: EF Core mutates it through its state machine, and `init`-only
record properties cannot be assigned by change tracking. Columns, beyond the state below:

| Member | Rule |
|---|---|
| `Id` (`MessageId`) | The end-to-end identity. A contract's is minted by the bus; a local message's is supplied by the sender (a domain event's `EventId`). |
| `Endpoint` | `null` = transport, `OutboxMessage.LocalEndpoint` (`"local"`) = the local endpoint. Any other value is dead-lettered. |
| `ContractName`, `Source` | Required for a transport row (wire type `<name>.v<n>`, emitting service); null on a local row. |
| `EventType` | The payload's **runtime** type, assembly-qualified. The local deserialization key; it never travels. |
| `Payload` | JSON text. The factory refuses a serializer whose content type is not JSON (`OutboxConfigurationException`). |
| `Headers` | The publication's own headers, reserved ones excluded (JSON column). |
| `TenantId` | `string?`, passed through as is. Null is valid in a single-tenant deployment; no global query filter. |
| `CorrelationId` | Non-nullable column. An absent or non-GUID correlation is replaced by `CorrelationId.New()`. |
| `CausationId` | Nullable. An absent or non-GUID causation is recorded as null. |
| `TraceParent` | `Activity.Current.Id` at publication. |
| `CreatedAtUtc` / `OccurredOnUtc` | Staging time (the claim's sort key) / business time (never claimed or ordered on). |

### State machine

```
Pending ──ClaimBatchAsync──► Processing
  Processing ─ dispatched ───────────────────► Published               terminal, ProcessedAtUtc set
  Processing ─ OutboxPayloadException ───────► Failed + DeadLettered   terminal, RetryCount unchanged
  Processing ─ transient, RetryCount+1 < Max ► Pending                 RetryCount++, NextRetryAtUtc
  Processing ─ transient, RetryCount+1 ≥ Max ► Failed + DeadLettered   terminal
  Processing ─ never attempted ──────────────► Pending                 Released: nothing counted
  Processing ─ its endpoint down ────────────► Pending                 deferred: RetryCount unchanged, NextRetryAtUtc = end of the endpoint's back-off
```

`Failed` always means terminal, with `DeadLettered = true` set together. There is no transient
`Failed` on the outbox. `IOutboxAdminStore.RequeueAsync` puts a dead-lettered row back to `Pending`
with `RetryCount` 0 and returns `true` only if a row was requeued.

### The atomic claim (`EfOutboxStore.ClaimBatchAsync`)

```csharp
// 1. Candidates — a separate query, never OrderBy/Take inside ExecuteUpdate (translation varies
//    by provider and version). Full rows, untracked, ordered on CreatedAtUtc.
var candidates = await Dispatchable(now).OrderBy(m => m.CreatedAtUtc).Take(batchSize).ToListAsync(ct);
var ids = candidates.ConvertAll(m => m.Id);
ids.Sort();   // one lock order for every processor: intersecting candidate sets cannot deadlock

// 2. Stamp — the atomic part. The eligibility predicate is REPLAYED inside the UPDATE:
//    UPDATE ... SET Status='Processing', LockedUntilUtc=@expiry, ClaimToken=@token
//    WHERE Id IN @ids AND NOT DeadLettered
//      AND (Status='Pending' OR (Status='Processing' AND LockedUntilUtc <= @now))
//      AND (NextRetryAtUtc IS NULL OR NextRetryAtUtc <= @now)

// 3. Read back by token — only when some candidates were lost. Winning all of them means the rows
//    in hand ARE the claim; they are patched in memory.
```

- Under `READ COMMITTED` a blocked `UPDATE` re-evaluates its `WHERE` against the committed row
  once the lock is released, so the loser does not match. That is what the token claim rests on
  instead of `FOR UPDATE SKIP LOCKED` — portable, and proven by the PostgreSQL concurrency tests.
- The `Processing AND LockedUntilUtc <= now` arm is crash recovery: without it a crashed loop's rows
  are stuck.
- SELECT + mutate + `SaveChanges` is **not** atomic under concurrent loops. Use `ExecuteUpdateAsync`.
- The claim is **cross-tenant** within a store: `TenantId` travels on the row and is never a claim
  filter.
- The claim orders on `CreatedAtUtc`, never `OccurredOnUtc`: a caller-supplied business time would
  let a backdated event jump the queue. `GetDeadLetteredAsync` orders on `OccurredOnUtc` on purpose —
  triage asks when the fact happened. Do not harmonise the two.

### Settlement

Every disposition is buffered as an `OutboxOutcome` (`readonly record struct`) and written by **one**
`ApplyOutcomesAsync` call per batch:

| Kind | Persisted | Retry budget |
|---|---|---|
| `Published` | `Status=Published`, `ProcessedAtUtc`, lease and token cleared | — |
| `Retry` | `Status=Pending`, `RetryCount`, `NextRetryAtUtc`, error text | consumed |
| `DeadLetter` | `Status=Failed`, `DeadLettered=true`, `ProcessedAtUtc`, error text | terminal |
| `Released` | `Status=Pending`, lease and token cleared, nothing else | **not consumed** |
| `Deferred` | `Status=Pending`, `NextRetryAtUtc` = the end of the endpoint's back-off, lease and token cleared; `RetryCount` and error text untouched | **not consumed** |

- **Every write filters on `ClaimToken`.** A loop whose lease expired matches zero rows instead of
  overwriting the loop that re-claimed the row. `ApplyOutcomesAsync` returns the affected-row count;
  fewer than the outcomes is logged as a partial settlement, never assumed to be success.
- `Published` and `Released` are one set-based statement each, `Deferred` one per distinct next
  attempt (in practice one per down endpoint); `Retry` and `DeadLetter` carry per-row values and cost
  one statement each, so the count scales with failures, not batch size or the length of an outage.
  A store provider settles all five kinds.
- The settlement runs as one operation of the context's execution strategy, in its own transaction
  (`SettlementTransaction`) — a retrying strategy refuses a transaction begun outside it — and joins
  an ambient transaction if one is open. An unknown commit is verified by the claim token, which the
  commit clears.
- Its token is an **independent** `OutcomeFlushTimeout`, never the host's stopping token: a
  cancelled settlement strands every lease of the batch. A settlement failure is logged and
  swallowed; leases expire and the batch is re-claimed.
- `Released` is what keeps an outage from spending the retry budget of messages it never reached.

### Dispatch (`OutboxProcessor`)

One execution scope **per row**, never shared across a batch. The scope's context is built from the
row: `TenantId` from the row, `CorrelationId` copied, `CausationId` = the row's `Id`. The processor
writes its store key on the scope's `DeliveryStoreHolder`, so a publication made there enlists in
the same store. Scope creation sits inside the per-row `try`: a tenant-aware factory may do I/O, and
its failure is that row's failure.

`IOutboxDispatcher` is **internal**. It is resolved keyed by `OutboxDispatchers.KeyOf(row)`:
`Endpoint == null` → `"microkit.outbox.transport"` (`TransportOutboxDispatcher`, registered with the
first store, and by `UseInMemoryTransport()`); `"local"` → `"microkit.outbox.local"` (`LocalOutboxDispatcher`, registered
with the first store); any other value → `OutboxPayloadException`.

| Outcome of a row | Verdict |
|---|---|
| dispatched | `Published` |
| `OutboxPayloadException` — unroutable endpoint, transport row without `ContractName` or `Source`, local payload no process can read | dead-letter **on first sight** |
| `OutboxTransportUnavailableException` — also a local consumer's dependency outage | on a row **with a tenant**, defer that tenant on the endpoint (below, #124); otherwise **defer** that row's endpoint: the row and its endpoint's later rows are settled `Deferred` — `NextRetryAtUtc` at the end of the endpoint's back-off, `RetryCount` and error text untouched — no retry consumed, the claim skips them; the other endpoint's rows are still dispatched |
| no dispatcher registered for the key (`GetKeyedService` returns null) → `OutboxConfigurationException` | release the rest of the batch (earlier rows keep their outcomes), settle it, **rethrow**: that store's outbox loop backs off on the idle curve and reports it each time (event 1402, `Critical`) — it never ends a loop. Every store registers both dispatchers, so a host composed through `AddMessageStore` never meets it: a missing transport is the activation row below |
| `GetKeyedService` throws `InvalidOperationException` (not `ObjectDisposedException`) | **defer** the tenant, or the endpoint, as above, **not** rethrown: `DispatcherActivationFailed`, event 1009 per failed activation (row's `MessageId`, `TenantId`, `Endpoint`; with no transport, its message names the call to add). A store with no transport still delivers its local rows |
| `OperationCanceledException` / `ObjectDisposedException` **while the host stops** (`HostStop`) | release the rest, nothing counted |
| anything else | transient: retry with back-off until `MaxRetries` |

- **Null and throwing are separate verdicts, decided by the container.** `GetKeyedService` returns
  null only when nothing is registered; it throws when something is registered and cannot be built.
  A `catch` around `GetRequiredService` would read a missing `IMessageTransport` behind the
  dispatcher as a missing registration and stop the loop.
- **Only `InvalidOperationException` is tagged as an activation failure.** Any other type thrown
  while the graph is built reaches the loop as if the dispatcher threw it. Widening that catch moves
  a per-tenant failure with its own type from a per-row retry into the endpoint-wide verdict.
- **Each endpoint has its own back-off** (`OutboxEndpointBackoff`, a singleton keyed by the store,
  outliving each cycle's scoped processor): it starts at `PollingInterval`, doubles on each failed
  attempt up to `TransportUnavailableBackoff`, and is cleared by the first row the endpoint
  dispatches; a failure a whole `TransportUnavailableBackoff` past its end starts a new outage from
  the base delay. While it runs, the processor passes the endpoint to the claim
  (`ClaimBatchAsync(…, OutboxEndpoints excluded, …)`) and the store leaves its rows out: they are
  neither claimed nor rewritten, a down broker is tried once per back-off period, and however many
  rows wait for it, the other endpoint's are claimed and delivered. The filter is part of the claim's
  contract — one signature, no default implementation. Rows a store hands over whose endpoint is
  in back-off are deferred unattempted: those of an endpoint the claim excluded (a store breaking the
  contract — this alone sets `HandedOverInBackOff`), and those of a back-off begun after the claim.
  Events: 1004 / 1009 why the endpoint is down, 1010 what is deferred and until when.
- **A down verdict is the tenant's first (#124).** Activation runs in the row's scope, built from its
  `TenantId`, and a broker can refuse one tenant, so both verdicts can be tenant-scoped. Each endpoint
  keeps its tenants in back-off (`OutboxEndpointBackoff.TenantsOf`, a `TenantBackoff` on the same
  curve). A down verdict on a row with a tenant blocks that tenant on the endpoint: its rows of the
  batch are settled `Deferred` unattempted, and until its back-off ends the processor passes it to
  the claim on that endpoint (`excludedTransportTenants` / `excludedLocalTenants`, from
  `TenantBackoff.BlockedAt`; the shared empty array when there is none), so its rows are neither
  claimed nor rewritten (A7): when the back-off ends one batch is claimed, its first row tried and the
  rest deferred again, so a one-tenant store touches one batch per back-off period. A row of a
  listed tenant handed over anyway is deferred and sets `HandedOverInBackOff`, as for an endpoint. The
  EF store sends each list as one array parameter (`EF.Parameter`: EF Core 10 otherwise sends one
  parameter per value), adds nothing for an empty list, and spells out `TenantId == null` so a row
  with no tenant is never left out, even under `UseRelationalNulls()`. **The bound:** the filter is
  residual, not an index range, so each claim reads, and never writes, a held tenant's claimable rows,
  as it reads past waiting rows (`TenantExclusionPlanTests`): its cost grows with that backlog, not the
  batch size. `TenantId` appended to the claim indexes changed neither plan nor buffers at 20,000 and
  200,000 rows (A8): do not add it. The exit for a tenant that will not recover is
  `DeadLetterPendingAsync`. A row with no tenant blocks the endpoint as above.
  - **The probe (half-open).** After a tenant is blocked, the next down verdict from a tenant with
    no entry — before any success — blocks that tenant **and** the endpoint, and clears the probe
    (`OutageScope.Shared`, event 1010). A success clears the probe. So a real outage costs at most two
    attempts per batch.
  - **P1, the order.** While a tenant has an entry, a batch attempts the rows of tenants with no
    entry first, in claim order, then the others by their entry's current delay (fewest failures),
    claim order on ties. One array and one list per batch; none with an empty map.
  - **P2, suspicion.** A tenant with an entry that fails again re-blocks only itself — unless the
    endpoint is *suspected* (it backed off and no success has cleared it since), where it counts as a
    probe like any other. Without P1, two tenants that never recover head every claim; without P2, a
    real outage costs one attempt per tenant once every tenant has an entry.
  - **Held.** A down verdict while the tenant's back-off is still in force — an attempt in flight
    when another blocked it, on another transport thread — changes nothing (`OutageScope.Held`): the
    same end, no growth of the curve, the probe untouched, and the deferred delivery logs 2014, not
    2013. Otherwise prefetched deliveries take one outage to the ceiling, and a suspected store's
    tenant trips the probe it set itself.
  - **C1.** A stop or a configuration fault releases the remainder of the attempt order, whichever
    pass was running.
  - Entries leave on their tenant's success, or once expired for a whole ceiling (pruned once per
    batch). Event 1011 reports, per batch and (endpoint, tenant), what waits. The operator's exit for a
    tenant that never recovers is `IOutboxAdminStore.DeadLetterPendingAsync`; nothing dead-letters a
    tenant on its own.
  - Do not copy the endpoint-wide verdict into the inbox: there, a guard that cannot be activated
    fails that one delivery or retry, never the batch.
- **Never throw `OutboxPayloadException`** for a broker nack, a timeout, a refused connection, an
  HTTP 503 or a database timeout. Only proven permanence dead-letters.

### `TransportOutboxDispatcher`

- Builds a `MessageEnvelope` from the columns — `Id`, `Source`, `Type` = `ContractName`, `Time` =
  `OccurredOnUtc`, `DataContentType` = `application/json`, `Body` = UTF-8 of `Payload` — with the
  row's own headers, then `tenantid`, `correlationid`, `causationid`, `traceparent` from the columns.
- Takes **no serializer and no registry**: the payload travels opaque, so the send path never
  depends on the producer's type graph. Consequence: a corrupt payload is not detected on the way out;
  it dead-letters at the consumer.
- `IMessageTransport` stays a **constructor** dependency. Resolved lazily, a missing transport would
  throw inside the dispatch and be classified transient — spending every row's retry budget.
- No `try`/`catch` around `SendAsync`: the transport's exception type is the classification.
- `SendAsync` returning means **the destination acknowledged**. `Published` is terminal; an
  asynchronous hand-off makes the mark a lie.

### Back-off — full jitter, both sides

```csharp
// n = the attempt count after this failure (outbox: RetryCount + 1; inbox: attempts)
// NextRetryAtUtc = now + Uniform(0, min(2^n seconds, MaxRetryBackoff))
TimeSpan ComputeBackoffCeiling(int n)
{
    var exponent = Math.Clamp(n, 0, 20);                  // a corrupt count cannot overflow
    var backoff = TimeSpan.FromSeconds(1L << exponent);
    return backoff > MaxRetryBackoff ? MaxRetryBackoff : backoff;
}
TimeSpan ApplyJitter(TimeSpan ceiling) =>
    ceiling <= TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromTicks((long)(ceiling.Ticks * random.NextDouble()));
```

- Outbox: `OutboxProcessor.ComputeBackoffCeiling` / `ApplyJitter`. Inbox: `InboxRetryPolicy`, shared
  by a failed first delivery and the retry loop. Same formula.
- Deterministic back-off resynchronises every row that failed together — exactly what an outage
  produces — on every later attempt. Linear back-off is a thundering herd.
- `TimeProvider` and `Random` are injected (`TryAddSingleton(TimeProvider.System)`,
  `TryAddSingleton(Random.Shared)`), so tests assert the curve against exact values.

---

## The receive pipeline (`MessageReceivePipeline`)

One pipeline for a transport delivery (`IEnvelopeReceiver` and the in-memory consume loop), a local
delivery (`DeliverLocalAsync`) and a store's retry (`RetryAsync`). Nothing is queued for a later
drain.

### Stages

1. **Once per message, before any scope:** resolve the wire type to a local type, parse `Id` as a
   GUID, deserialize the body. Any failure here is permanent — dead-lettered, no handler reached.
   `EnvelopeReceiver` also dead-letters a non-JSON `DataContentType` before the pipeline.
2. No handler registered: success, logged at `Warning` (3001) and counted as
   `microkit.inbox.envelopes.unconsumed`. Correct, and never silent.
3. **Per handler, in its own scope** (`IExecutionScopeFactory`): the execution context is written
   (tenant from `tenantid`, correlation from `correlationid` — minted once per delivery when absent —
   causation = the incoming message id) and, for a guarded handler, the delivery's store; then every
   `IMessageScopeInitializer` runs, in registration order; then the `IMessageReceiveMiddleware`
   chain; then the handler, resolved last.
4. With a store, each handler is **guarded** (below). Without one, it simply runs.

### The inbox guard — a new delivery (`DeliverGuardedAsync`)

1. An outer scope; resolve the store's keyed `IInboxGuardStore`. `FindAsync` the
   (`MessageId`, `ConsumerType`) row, untracked. **A row in any state** — processed, failed,
   retrying, dead-lettered — **skips the handler** (2200, counted deduplicated): a redelivery never
   starts a second attempt.
2. `IInboxGuardStore.ExecuteAsync` runs the attempt as **one operation of the store's execution
   strategy**. Each attempt is a **fresh scope**: initializers, then `BeginAsync`, then
   `EnlistProcessedAsync(row)` (the row already `Processed`), then middlewares and handler, then
   `CommitAsync` (save, then commit). The row, the handler's saves, its publications and its cascade
   events commit together.
3. A commit whose outcome is unknown is **verified by reading the row** (`Processed` → committed),
   never replayed blindly.
4. On failure, unless it is a dependency outage, read the row again:
   - this attempt's own `RowId` → it committed, then faulted (2206): success;
   - another row → a concurrent delivery committed first and this save hit the unique index (2201):
     rolled back, success;
   - none → record the failure.
5. **Recording a failure** (`RecordFailureAsync`): the handler's transaction has rolled back; a new
   scope inserts the row `Failed` with `TryInsertFailedAsync` — its own transaction, never an upsert.
   Only once that row is committed is the delivery a success for the transport. From then on the
   store's inbox loop owns the retries.
   - **Permanent** (`PermanentMessageException`, `InboxPayloadException`): written dead-lettered,
     `RetryCount` 0, `ProcessedAtUtc` set (2203). A permanent verdict counts no attempt.
   - **Transient**: `RetryCount` 1, `NextRetryAtUtc` from the back-off (2202) — or dead-lettered at
     once if `MaxRetries` is 1.
   - **Dependency unavailable** (`InboxDependencyUnavailableException`) on a delivery **with a
     tenant** (#124 D6, #210): its (consumer, tenant) pair is blocked in the store's inbox state
     (`InboxBackoffGroup`, one `InboxBackoff` per store or instance, shared with the retry loop and
     thread-safe) — and its consumer for every tenant when its consumer probe fails: a second tenant new
     to it, not the store probe failing — another consumer succeeded since, or that tenant already failed
     for another consumer (2015) — and the row is written `Failed`, `RetryCount` 0,
     `NextRetryAtUtc` at the end of the back-off (2013); the delivery is acknowledged once that row
     committed. A later delivery a back-off holds runs `FindAsync`, then writes the same row **without
     running that handler** (2014, `Debug`). **No row** — the transport pauses and redelivers, nothing
     counted — for a delivery with no tenant, a local delivery (the outbox defers it, D1), the store
     unreachable, the store probe failing (a second tenant down before any success; a probe set by a consumer held for
     every tenant and still on record — not cleared by its success nor pruned — is failed only by another consumer's
     failure, unless the store is suspected), or a deferred row
     that cannot be written. A success of a consumer for a tenant clears that pair, that consumer's
     probe and back-off, and the store probe — never another consumer's back-off.
   - A row already there → another delivery owns it (2208): success.
   - The `Failed` row cannot be written (2204): transient — the transport redelivers, counted.
   - **The store reports itself unreachable** — `FindAsync` or `TryInsertFailedAsync` throws
     `InboxDependencyUnavailableException` (2209): a dependency outage, nothing charged; the transport
     pauses and redelivers, a local row is deferred under the local endpoint's back-off, never
     dead-lettered. Only the store can tell an outage from a write that fails on its data (a value
     too long for its column fails every time, and must dead-letter, not pause for ever). The EF store
     raises it when its connection cannot be opened, or when the provider calls the fault transient
     (`DbException.IsTransient`).
6. The guard not registered for the store (2207) is transient here; host start rejects it first.

> **Scope initializers run before the attempt's transaction.** They configure the scope the store's
> `DbContext` is built from — a tenant's connection, say — and that context begins the transaction.
> Everything that writes (middlewares, handler) runs inside it.

### Verdicts and dispositions

`DeliveryVerdict`, ordered least to most redeliverable; several handlers yield the maximum.

| Verdict | Raised by | `ReceiveDisposition` |
|---|---|---|
| `Succeeded` | every handler done, skipped, or its failure recorded on its row; or no handler | `Acknowledge` |
| `Permanent` | unknown wire type, non-GUID id, unreadable body; an **unguarded** handler's permanent exception (`EnvelopeReceiver` also answers `DeadLetter` for non-JSON content, before the pipeline) | `DeadLetter` |
| `Transient` | an unguarded handler's other exception; a guarded failure that could not be recorded | `Redeliver` |
| `DependencyUnavailable` | `InboxDependencyUnavailableException` | `PauseAndRedeliver` |
| `Unsettled` | host stop | `Unsettled` — never an acknowledgement |

- A guarded handler whose failure is recorded yields **success**: the store retries it, not the
  transport.
- `EnvelopeReceiveResult` carries `Disposition`, `Reason`, `Error`; built by `From(...)`, with the
  shared `Acknowledged` instance.
- `Acknowledge` means every handler's outcome is durable. A provider settles the broker message only
  after `ReceiveAsync` has completed — the mirror of `SendAsync`'s rule.
- **Shutdown is not a failure** (`IsShutdown`): an `OperationCanceledException` while the host token
  is cancelled, or an `ObjectDisposedException` while it is cancelled or naming `IServiceProvider`,
  stops the delivery with `Unsettled`, never classified, never counted. One raised by a handler's
  own object while the host runs is an ordinary failure.

### `IInboxGuardStore` — the contract a store implements

Scoped, keyed by the store. One instance per scope, one scope per attempt.

| Member | Contract |
|---|---|
| `FindAsync(key)` | Reads the row's state (`InboxRowState`: `Status`, `DeadLettered`, `RowId`) **from the database**, untracked — never from a cache or tracker: it answers "did an unknown commit land?". Throws `InboxDependencyUnavailableException` when the database cannot be reached. |
| `ExecuteAsync(state, op, verify, verified)` | One operation of the execution strategy; `state` is handed to both callbacks, so the engine passes static lambdas; after an unknown-outcome fault, `verify` first. The last fault propagates **unwrapped** (no `RetryLimitExceededException`). |
| `BeginAsync` | Begins the attempt's transaction. The EF store throws if the context already has one. |
| `EnlistProcessedAsync(row)` | Adds a new delivery's row, already `Processed`. Writes nothing. The unique index is the guard. |
| `MarkClaimedProcessedAsync(key, token)` | Marks a retry's claimed row `Processed`, **conditioned on the claim token**; `false` when the row no longer carries it — the handler must not run. Must hold under a context whose queries do not track by default. |
| `CommitAsync` | Saves, then commits. The EF store re-stages the row if the handler cleared the tracker, and only if this attempt did not already write it. |
| `IsLeaseLost(ex)` | Structural: the conditioned write matched no row (EF: `DbUpdateConcurrencyException` on an `InboxMessage` entry). Any other exception, a business unique violation included, is the handler's failure. |
| `TryInsertFailedAsync(row)` | Inserts a failed row and commits it alone; `false` when a row of that pair exists. Throws `InboxDependencyUnavailableException` when the database cannot be reached; any other fault propagates as is. |

### Handler rules with a store

- **Do not begin a transaction on the store's context.** The engine's is open;
  `ITransactionalContext.ExecuteAsync` joins it and its commit is the engine's.
- `IResultUnitOfWork.TryCommitAsync` throws `InvalidOperationException` inside a guarded handler (an
  open transaction); `TryExecuteAsync` joins it and converts nothing. Save with
  `IUnitOfWork.CommitAsync`, or let the engine save.
- `LeaseDuration` must exceed the worst-case handler duration: a retry that loses its claim
  mid-handler (2205) records nothing and counts nothing.

---

## Inbox

### `InboxMessage`

`sealed class`, `{ get; set; }`, for the same reason as `OutboxMessage`.

| Member | Rule |
|---|---|
| `RowId` (`Guid`) | **Primary key**, writer-assigned, `ValueGeneratedNever`. The claim selects on it alone: a compound-key `UPDATE` filtered by two `Contains` selects the cross product and breaks the `batchSize` bound. |
| `MessageId` + `ConsumerType` | Unique index `UX_InboxMessages_MessageId_ConsumerType` — **the sole authority** for deduplication. `ConsumerType` is the handler type's version-free key, `Namespace.Type, AssemblyName` (`MessageConsumerKey`): an assembly-version bump never changes it. |
| `EventType` | The local type the message was delivered as, assembly-qualified. |
| `Payload`, `Source`, `Headers` | What a retry rebuilds its `MessageContext` from. |
| `TenantId` | `string?`, no filter, never a claim filter. |
| `CorrelationId` | Nullable; a correlation that is not a GUID is not recorded in the column and survives in `Headers`. |
| `CausationId` | **Copied** from the envelope — the incoming message's own cause, as data. |
| `ReceivedAtUtc` / `OccurredOnUtc` | Local receipt (the claim's sort key) / producer's business time (never indexed, claimed or ordered on). |
| `ClaimToken` | **EF concurrency token** (`IsConcurrencyToken()`). |

`OutboxMessage.CorrelationId` is non-nullable and `InboxMessage.CorrelationId` nullable. The
asymmetry is deliberate; do not "fix" it.

### Status machine — one row per (message, handler), written once

```
new delivery ─ success ─────────► Processed                   in the handler's transaction
new delivery ─ failure ─────────► Failed (RetryCount 1)        own transaction, after rollback
new delivery ─ permanent ───────► Failed + DeadLettered (0)    own transaction
Failed ─ ClaimBatchAsync ───────► Processing
  Processing ─ success ─────────► Processed                   MarkClaimedProcessedAsync, handler's transaction
  Processing ─ transient ───────► Failed, RetryCount++         or Failed + DeadLettered at MaxRetries
  Processing ─ permanent ───────► Failed + DeadLettered        RetryCount unchanged
  Processing ─ never attempted ─► Failed                      Released: nothing counted
```

`Received` is written by no path; the claim still picks it up so a row from an earlier version is
handled. Terminal = `Processed`, or `Failed` with `DeadLettered = true`.
`IInboxAdminStore.RequeueAsync` sets `Failed`, `DeadLettered = false`, `RetryCount` 0 and clears
the back-off, so the loop picks it up.

### The retry loop (`InboxProcessor`)

It never invokes a handler itself. It claims a batch, reads each row back (registry lookup of the
(`ConsumerType`, message type) pair — bus handlers first, then local consumers — deserialization,
`MessageContext` rebuilt from the row), and runs it through `MessageReceivePipeline.RetryAsync`. A
success commits its own row and produces **no** outcome; only failures and releases are buffered and
settled in one `ApplyOutcomesAsync`.

| Result of a row | Effect on the row | Effect on the batch |
|---|---|---|
| processed | committed by the attempt | continues |
| lease lost (`MarkClaimedProcessedAsync` false, or `IsLeaseLost`) — 2205 | untouched, owned elsewhere | continues |
| permanent — `PermanentMessageException`, `InboxPayloadException`, unknown consumer, type that no longer resolves, unreadable payload | dead-letter **now**, `RetryCount` unchanged | continues |
| transient (anything unrecognised) | `Retry` with back-off; `DeadLetter` at `MaxRetries` | continues |
| `InboxDependencyUnavailableException`, a row with a tenant (#124 D5, #210) | `Retry` with `RetryCount` unchanged, `NextRetryAtUtc` at the end of its pair's back-off, or its consumer's (2015); the later rows of the batch it holds the same, unattempted (2013 per pair); the pair, or the consumer's rows with a tenant, left out of the claims until then (`InboxClaimExclusions`, A7) | continues; counted as released |
| `InboxDependencyUnavailableException`: no tenant, the store unreachable (`RetryOutcome.StoreUnreachable`, set when the guard's verify read throws it), or the store probe failing | released, nothing counted | abandoned; back-off toward `DependencyUnavailableBackoff`; the store's inbox suspected |
| `InboxConfigurationException` (no guard for the store) | released | abandoned, rethrown: that store's inbox loop backs off on the idle curve (1402), and never ends |
| host stop | released, nothing counted | abandoned |

- An `ObjectDisposedException` naming the provider while the host **runs** is a failed attempt on a
  retry (only `HostStop` is a stop), unlike on the transport path.
- **Never throw `InboxPayloadException`** for a timeout, a refused connection, an HTTP 503, a
  database timeout or a deadlock.

### Inbox claim and settlement (`EfInboxStore`)

Same shape as the outbox — candidates by `RowId` ordered on `ReceivedAtUtc`, sorted, stamped with the
eligibility predicate replayed (`NOT DeadLettered AND (Status IN (Failed, Received) OR (Processing
AND LockedUntilUtc <= now)) AND NextRetryAtUtc` reached), the consumers and the (consumer, tenant)
pairs in back-off left out of the candidates (#210: one array parameter of consumers, one of keys
`ConsumerType + U+001F + TenantId`, exact because no consumer key contains U+001F; never a row with no
tenant) — with two differences:

- The read-back by token always runs, and fills a key → `RowId` map. **Settlement must run on the
  same store instance that claimed** (the loop's scope): an outcome whose key is not in the map is
  skipped.
- `Retry` and `Released` set `Status = Failed`, not a pending state: `Failed` with
  `DeadLettered = false` *is* the awaiting-retry state.

### The claim token does two jobs

1. **Fencing.** Every write filters on it, so a loop whose lease expired matches zero rows.
2. **Settled marker.** A committed retry leaves it null, so any later write for that row — a
   `Released` issued while the host stops, after the handler had committed — filters to zero rows.

Job 1 for the success write works only because `ClaimToken` is mapped as an **EF concurrency
token**: `MarkClaimedProcessedAsync` is a tracked update that `SaveChanges` emits, and without the
token that `UPDATE` carries the primary key alone and the lost update returns. Removing the mapping
breaks no test that does not exercise concurrency. The outbox needs no such mapping: all its writes
are `ExecuteUpdateAsync` with an explicit token filter.

---

## The local endpoint (`Local/`)

An in-process message, never handed to a transport and needing no `[MessageContract]` — today, a
domain-event notification sent by `MicroKit.Messaging.MediatR` through `ILocalMessageSender`. Its
consumers come from `ILocalConsumerSource` (`LocalConsumer`: message type, consumer name, handler
type, invoker), read once into `LocalConsumerRegistry` and matched by **exact** runtime type.

- **With a store:** `LocalMessageSender` resolves the store as a publication does and enlists an
  outbox row with `Endpoint = "local"` (`OutboxMessageFactory.CreateLocal`; tenant, correlation and
  causation from the scope's `IExecutionContext`). The store's outbox loop dispatches it through
  `LocalOutboxDispatcher` → `DeliverLocalAsync`: one **guarded** delivery per consumer, each under
  its own inbox row. Returning marks the outbox row `Published`. A verdict the pipeline could not
  record becomes the outbox's: permanent → dead-letter the row; dependency → defer the local
  endpoint (its transport rows still go out);
  failure no inbox row could hold → retry the row (consumers already done skip on their rows).
- **Without a store:** `LocalQueue`, in memory — one entry per (message, consumer), each with its
  own attempt count (`MaxDeliveryAttempts` 5), dead letter (2302) and dependency pause (5 s, 2304),
  delivered by `LocalQueueWorker` (registered with the first `AddLocalConsumerSource`). Queued
  **before the sender's work commits** — a rolled-back command still notifies — and lost if the
  process stops. No consumer at all: counted unconsumed, never queued.
- A local delivery's `MessageContext` has an empty `ContractName` and `ContractVersion` 0.

A batch replay costs a re-send, never a re-run: every consumer of a transport row sits behind the
receiving inbox, and every consumer of a local row behind its own row.

---

## The per-message context — correlation copied, causation derived

| Where | Execution context `CausationId` | Never |
|---|---|---|
| `OutboxProcessor` (dispatch scope) | the row's `Id` | the row's `CausationId` — the grandparent |
| Pipeline, transport delivery | the envelope's `Id` | the envelope's `causationid` header |
| Pipeline, local delivery and retry | the message's `MessageId` | the row's `CausationId`, and never `RowId` |

- `CorrelationId` is copied everywhere. At the receiving end it is **minted once per delivery** when
  absent, so every handler of the message — and everything they publish — shares one chain.
- The inbox row's `CausationId` and `MessageContext.CausationId` are the incoming message's own
  cause, copied as data. Deriving them would overwrite the producer's link.
- A test of causation must seed a **different** ancestor causation, or copy-through passes too.
- `RowId` is meaningless outside its table; `MessageId` is the identity that answers "which message
  caused this" downstream.

---

## The engine

`MessageStoreEngine` (`BackgroundService`, `IServiceScopeFactory` only) reads the store set once and
runs, **per store**, four independent loops: `OutboxLoop`, `InboxLoop`, `OutboxRetentionLoop`,
`InboxRetentionLoop` (event 1403 lists the stores). Each loop creates its own scope per cycle and
resolves its store's keyed processor or retention store from it, so **a store whose database is
down backs off alone**.

**Partitioned stores (#191, ADR-MSG-021).** For a partitioned store the engine runs a
`StoreInstanceReconciler` instead: it reads the source's snapshot at start and on each change of its
token (signals coalesced in a bounded channel of one, `DropWrite`; a faulty snapshot is logged, 1407 /
1408, and ignored) and runs the same four loops per instance, each with its own `StoreInstanceState`
— outbox endpoint back-off, inbox back-off (its pairs and consumers, #210), metric series — created when the instance appears
and released once its loops have ended. Each loop scope is prepared for the instance (store and
instance on `DeliveryStoreHolder`, then the instance initializer, then the store's pending instance
check); the processors resolved there take the instance's state. A removal stops each loop between
batches: the batch in flight settles on the host's token, never cancelled. The outbox and inbox batches
of every instance pass one `FifoGate` (`StoreInstanceOptions.MaxConcurrentBatches`, 16), held claim to
settlement and released in a `finally`; a released permit goes to the oldest waiter. Single stores
never enter it. A delivery guarded by a partitioned store runs against its instance — a local row's
own, else the source's mapping, a lookup that never creates state — and one mapped to no instance
drained here is `Transient` (1410), never a pause. Instance loops log 1411–1414 with `{Store}` and
`{Instance}`, each iteration inside a logging scope carrying both.

| Cadence (outbox; the inbox the same, plus its outage arm) | Next delay |
|---|---|
| inbox only: dependency unavailable | grow toward `DependencyUnavailableBackoff` |
| cancelled | unchanged |
| outbox only: rows handed over in back-off (`HandedOverInBackOff`) | `PollingInterval` |
| saturated (claimed ≥ `BatchSize`) | 0 — poll again at once |
| empty | grow toward `MaxPollingInterval` |
| partial | `PollingInterval` |
| the cycle threw (claim failed: database unreachable) — 1401 | grow toward `MaxPollingInterval` |

Grow = double, starting from `PollingInterval`, capped. The outbox has no outage arm: a down endpoint
backs off on its own rows (above), so a batch that hit one is sized like any other — a full one
cannot re-claim the rows it deferred. One exception, before saturation: a batch carrying rows handed
over in back-off (`OutboxBatchResult.HandedOverInBackOff` — a store breaking the claim's contract by
not honouring its endpoint filter) waits `PollingInterval`, whatever its endpoint verdict
(cancellation aside) and even if it delivered something, or it would rewrite the down endpoint's
backlog back to back. Such a store still pays a rewrite of the down endpoint's backlog each back-off
period, and holds the other endpoint to one batch per `PollingInterval` while it keeps handing that
backlog over: the filter is required. A back-off that began within the batch sets no flag: its
deferred rows wait out their next attempt, a conforming store leaves the endpoint out of the next
claim, and a non-conforming one's next batch sets the flag if it comes within the back-off (after a
saturated batch). The EF store filters with `IX_OutboxMessages_Endpoint_Dispatchable`
(`Status, Endpoint, CreatedAtUtc`), and the local exclusion is a `UNION ALL` of ranges around
`'local'`: `<> 'local'` walks the local backlog (`EndpointExclusionPlanTests`, both directions,
asserted on buffers). A processor that cannot be activated (1400) grows toward
`TransportUnavailableBackoff`.

- **Activation is not registration.** A store missing a required service fails at start. An
  exception while resolving a registered processor or retention store is logged as an activation
  failure (1400) with back-off; the loop keeps running.
- **A configuration fault backs off, and never ends a loop** (1402, `Critical`, logged on every
  cycle): the processor settles first — the rest of the batch released, earlier rows keeping their
  outcomes — then rethrows, and the loop grows its delay toward `MaxPollingInterval`. It cannot clear
  without a fix; start validation rejects the compositions that raise it.
- **A host stop is never a failure.** The loop ends only on the host's token; a processor
  interrupted by it releases what it claimed with attempt counts unchanged (`HostStop`).
- **One loop guard** (`GuardedLoop`) runs every long-running loop — the four store loops,
  `LocalQueueWorker`, `InMemoryTransportWorker`: a thrown exception ends the loop only when
  `HostStop` says so; any other is a failed cycle with that loop's back-off, and is logged (1401 for
  a store loop, 1402 a configuration fault, 2305 the local queue, 3017 the in-memory transport). An
  unsettled delivery while the host runs is, in the in-memory workers, a dependency outage (requeued,
  nothing charged, paused) only when the root container cannot create a scope — disposed, or the
  probe failing any other way, which never escapes — and otherwise a counted failure (a handler's own disposed provider), never a busy loop; logging goes through
  `GuardedLoop.Log`, so a logging provider that throws never ends a loop. No loop keeps a catch shape
  of its own.

### Retention — not housekeeping

| | Deletes | `RetentionDays` default | Why |
|---|---|---|---|
| Outbox | `Published` rows with `ProcessedAtUtc` older than the cutoff, every tenant (`tenantId: null`) | 7 | early deletion loses history only |
| Inbox | `Processed` rows, same rule | **30** | the table only deduplicates what it still holds: the window must exceed the longest plausible redelivery delay of every upstream transport |

Do not harmonise the two. `RetentionDays <= 0` disables the loop. Failed and dead-lettered rows are
never deleted by retention. `tenantId: null` means every tenant, and is the only value that matches
rows in a single-tenant deployment.

### Options

`MessagingOptions` (section `MicroKit:Messaging`) carries `Outbox` (`OutboxProcessorOptions`) and
`Inbox` (`InboxProcessorOptions`), both `sealed record` with `{ get; set; }` — `init` would make the
`bus.Configure(...)` callback a no-op. Global: every store's loops use them. Validated at start.

| | Outbox | Inbox |
|---|---|---|
| `BatchSize` | 100 | 100 |
| `PollingInterval` / `MaxPollingInterval` | 5 s / 1 min | 5 s / 1 min |
| outage back-off | `TransportUnavailableBackoff` 5 min — the ceiling of each endpoint's back-off | `DependencyUnavailableBackoff` 5 min — the loop's |
| lease | `LockDuration` 5 min | `LeaseDuration` 5 min |
| `MaxRetries` / `MaxRetryBackoff` | 5 / 1 h | 5 / 1 h |
| `OutcomeFlushTimeout` | 10 s | 10 s |
| `MaxErrorMessageLength` | 2000 | 2000 |
| `RetentionDays` / `RetentionInterval` | 7 / 1 h | 30 / 1 h |

---

## Removed in #188

Do not reintroduce or name as current: `IIntegrationEventPublisher`, `IIntegrationEventWriter`
(`HasOpenTransaction`), `IntegrationEventWriteResult`, `IInboxWriter`, `InboxWriteResult`,
`IInboxSettlementStore`, `MessageKind`, `OutboxDispatcherKeys`, `MediatROutboxDispatcher`,
`DomainEventsCascadeNotificationPublisher`, `OutboxMessage.OriginMessageId` and the index
`UX_OutboxMessages_Origin_ContractName`, the outbox/inbox workers and coordinators,
`AddEfCoreOutbox`, `AddEfCoreIntegrationEvents`, `AddMediatRDomainEvents`.
