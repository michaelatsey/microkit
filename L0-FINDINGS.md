# L0 Findings — end-to-end integration harness

Findings surfaced while building `MicroKit.Messaging.MediatR.IntegrationTests`, the first test to
run the domain-event → outbox → notification path as one system.

These are **findings, not decisions**. Nothing here has been fixed, and no ADR has been written: an
ADR would consign a decision that has not been taken. No production code was modified.

Reproduced by: `dotnet test modules/MicroKit.Messaging/MicroKit.Messaging.slnx -c Release`

---

## Finding #1 — `AddMicroKitMessaging()` requires `ILogger<T>` but never registers logging

**Severity:** low (composition-time, fails loudly, easy host-side workaround).

### Exact exception

```
System.InvalidOperationException : Unable to resolve service for type
'Microsoft.Extensions.Logging.ILogger`1[MicroKit.Messaging.Processing.OutboxProcessor]'
while attempting to activate 'MicroKit.Messaging.Processing.OutboxProcessor'.
```

### The six types that require `ILogger<T>`

| Type | File | Fails when |
|---|---|---|
| `OutboxProcessor` | `modules/MicroKit.Messaging/src/MicroKit.Messaging/Processing/OutboxProcessor.cs:38` | first outbox drain |
| `InProcessMessagePublisher` | `modules/MicroKit.Messaging/src/MicroKit.Messaging/Publishing/InProcessMessagePublisher.cs:42` | first outbox drain |
| `MediatROutboxDispatcher` | `modules/MicroKit.Messaging/src/MicroKit.Messaging.MediatR/Outbox/MediatROutboxDispatcher.cs:37` | first outbox drain |
| `InboxProcessor` | `modules/MicroKit.Messaging/src/MicroKit.Messaging/Processing/InboxProcessor.cs:54` | first inbox drain |
| `OutboxWorker` | `modules/MicroKit.Messaging/src/MicroKit.Messaging/Processing/OutboxWorker.cs:34` | host `StartAsync` |
| `InboxWorker` | `modules/MicroKit.Messaging/src/MicroKit.Messaging/Processing/InboxWorker.cs:34` | host `StartAsync` |

### When it surfaces: first resolution, not registration

`AddMicroKitMessaging()` returns normally — MS DI validates nothing at registration time. The throw
comes when the container first *activates* one of the six. With `ValidateOnBuild = true` it moves
forward to `BuildServiceProvider`; with `ValidateOnBuild = false` (this suite) it surfaces at the
first drain, i.e. potentially long after startup.

### Which hosts are affected

- **Not affected** — anything wiring logging by default: `WebApplication.CreateBuilder()`,
  `Host.CreateDefaultBuilder()`, `Host.CreateApplicationBuilder()`. A conventional console worker
  built with `Host.CreateApplicationBuilder()` **does** get logging and is fine.
- **Affected** — a bare `new ServiceCollection()` composed by hand (this test suite), and a bare
  `new HostBuilder()` with no `ConfigureLogging`.

### Related inaccuracy

`AddDbContext` does **not** register the open-generic `ILogger<>` in the application container. The
comment in `TransactionBehaviorPersistenceTests.BuildProvider` stating "AddDbContext already calls
AddLogging()" is wrong. Harmless there — that container never activates a type needing
`ILogger<T>` — but it is what made this gap non-obvious. Disproved empirically: `AddDbContext` had
already run when the resolution threw.

**Workaround in use:** the test harness calls `services.AddLogging(...)` explicitly.

---

## Finding #2 — the inbox dedup gate is documented but not implemented; outbox redelivery can never succeed

**Severity: high.** This defeats the at-least-once guarantee the outbox exists to provide.

Observed by `InboxDuplicateObservationTests.Redelivery_AfterInboxRowsWritten_SecondDispatchBehaviour`.

### The documented contract

`modules/MicroKit.Messaging/src/MicroKit.Messaging.EntityFrameworkCore/Stores/EfInboxStore.cs:29`

> The compound PK `(MessageId, ConsumerType)` is the dedup gate: a `DbUpdateException` on duplicate
> **must be caught by the caller** (`InProcessIntegrationDispatcher`) and treated as a dedup skip.

`modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/IInboxStore.cs:32` says the same.

### What the code actually does

`DbUpdateException` appears **only in those two XML doc comments**. No code anywhere in
`MicroKit.Messaging` catches it:

```
grep -rn "DbUpdateException" modules/MicroKit.Messaging/src --include="*.cs"
→ 2 hits, both XML doc comments, zero catch clauses
```

`InProcessIntegrationDispatcher.DispatchAsync` — the named caller — has no try/catch. Neither does
`InProcessMessagePublisher.PublishAsync`, which is what actually calls `_inboxStore.AddAsync`.

### Observed behaviour

An integration event published once, with two registered consumers, then the same outbox row
redelivered (status reset to `Pending` — exactly what lease expiry after a crash produces):

```
exception escaping drain 2 : (none)
inbox rows after drain 1   : 2
inbox rows after drain 2   : 2
outbox status after drain 2: Pending
outbox RetryCount          : 1
outbox DeadLettered        : False
outbox ErrorMessage        : An error occurred while saving the entity changes. See the inner exception for details.
```

Underlying error:

```
Microsoft.Data.Sqlite.SqliteException (0x80004005): SQLite Error 19:
'UNIQUE constraint failed: InboxMessages.MessageId, InboxMessages.ConsumerType'.
```

`OutboxProcessor`'s catch-all converts it into a retry decision:

```
[Warning] MicroKit.Messaging.Processing.OutboxProcessor: Outbox message
20164e16-185c-4e67-8e8e-84281bfed918 (…WidgetSyncedEvent…) failed dispatch
(attempt 1/10). Will retry. (DbUpdateException)
```

### Why this matters

The redelivery is **not** an error condition — it is the normal consequence of the at-least-once
guarantee (lease expiry after a crash, or any transient failure between dispatch and
`MarkPublishedAsync`). The message was already correctly delivered; both inbox rows exist.

But every retry re-attempts the same duplicate insert and fails identically. With
`MaxRetries = 10` the row exhausts its retries and is dead-lettered — `Status = Failed`,
`DeadLettered = true`, terminal — for a message that was delivered successfully the first time.
The outbox permanently dead-letters correctly-delivered messages, and the operator sees a
dead-letter queue full of false failures.

The dedup gate is the one thing that makes redelivery idempotent, and it is missing.

---

## Finding #3 — cascade domain events are staged to the outbox but never flushed, and are silently lost

**Severity: high.** Silent data loss — no row, no exception, no log line.

Observed by `CascadeObservationTests.Cascade_NotificationHandlerRaisingDomainEvent_SecondDrainProducesNothing`.

### Mechanism

`DomainEventsCascadeNotificationPublisher`
(`modules/MicroKit.Messaging/src/MicroKit.Messaging.MediatR/Events/DomainEventsCascadeNotificationPublisher.cs`)
runs all handlers for a notification, then calls `IDomainEventsDispatcher.DispatchEventsAsync` so
that domain events raised *by those handlers* are dispatched. Its own XML doc describes this as the
supported cascade scenario:

> new P3/P4 outbox rows are staged — all within the same outbox processor scope, without committing
> a new transaction.

`DomainEventsDispatcher` P4 calls `IOutboxWriter.AddBatchAsync`, which only *stages* rows in the EF
Core change tracker. **Nothing on the outbox processing path ever calls `SaveChanges` on that
context.** `TransactionBehavior` is the sole flush owner (ADR-MSG-012) and is not in this path —
it only wraps command dispatch. The per-message execution scope is then disposed, and the staged
rows die with it.

### Observed behaviour

A command raises one domain event; its notification handler raises a second domain event on a newly
tracked aggregate:

```
outbox rows after command      : 1
outbox rows after drain 1      : 1 [Published]
outbox rows after drain 2      : 1 [Published]
cascade-raising handler ran    : 1 time(s)
cascade notification handler   : 0 time(s)
Warning+ during drain 1        : 0
Warning+ during drain 2        : 0
```

The cascade handler definitely ran and definitely raised its event. No second outbox row is ever
written, so the cascade event's own notification handler is never invoked. Nothing at `Warning` or
above is logged on either drain — the loss is completely silent.

### Related observation (inferred, not demonstrated by this test)

`EfInboxStore.AddAsync` *does* call `SaveChangesAsync`, and its own XML doc already warns that this
flushes **all** tracked changes on `TContext`, not just the inbox row. The cascade scenario above
never reaches that code — the notification branch of `MediatROutboxDispatcher` writes no inbox rows,
so nothing flushes and the loss is total.

But the two facts sit on the same scoped `DbContext`. If anything within a single per-message scope
did trigger an inbox write — a notification handler publishing an integration event, for instance —
that `SaveChangesAsync` would flush any staged cascade rows as a side effect. That path is **not
covered by this test and has not been reproduced**; it is flagged only because it would make the
behaviour non-deterministic (sometimes lost, sometimes flushed) rather than reliably absent, which
matters for how a fix is chosen. Worth a dedicated test before acting on it.

---

# L1 Findings — outbox claim rewrite

Surfaced while rewriting the outbox (`feature/messaging/outbox-claim-rewrite`). Same standing as the
findings above: **nothing here was fixed**, and each is recorded precisely because acting on it was
out of scope for that lot.

---

## Finding #4 — `AddMicroKitMessaging`'s options callback is a silent no-op

**Severity: high.** A shipped defect in `1.0.0-preview.4`, not a style problem.

`ServiceCollectionExtensions.AddMicroKitMessaging` accepts
`Action<OutboxProcessorOptions>? configureOutbox`, and `OutboxProcessorOptions` was declared with
`init`-only accessors:

```csharp
public sealed record OutboxProcessorOptions
{
    public int BatchSize { get; init; } = 20;   // init — assignable only in an initializer
    ...
}
```

An `Action<T>` receives the already-constructed instance. `init` accessors are assignable only
during object initialization, so the callback body **cannot assign any property**. A `with`
expression inside the lambda produces a new record that is immediately discarded — the instance
registered in the container is the default one.

The consequence is not a compile error, a warning, or an exception. Every consumer who wrote

```csharp
services.AddMicroKitMessaging(o => o.BatchSize = 500);   // does not compile
services.AddMicroKitMessaging(o => o = o with { BatchSize = 500 });  // compiles, does nothing
```

got the defaults, silently. Any deployment that believed it had tuned its outbox had not.

**Status:** closed by the L1 rewrite as a *consequence* rather than as its purpose — the design
prescribes `{ get; set; }` accessors, which makes the callback work. It is recorded here as its own
finding so the defect is not buried inside a rationale about record conventions, and so that anyone
auditing `1.0.0-preview.4` in the field knows their outbox configuration never applied.

`InboxProcessorOptions` has the same shape and the same defect. **It was not touched** — the inbox is
out of scope for this lot — so `configureInbox` remains a silent no-op today.

---

## Finding #5 — the ADR immutability rule contradicts its own precedent

**Severity: low** (process, not runtime), **but it invalidates an audit assumption.**

`.claude-context/context/microkit-architectural-decisions.md:4` states:

> Each ADR is immutable once merged; superseded decisions reference the ADR that replaces them.

Those two clauses cannot both hold. "References the ADR that replaces them" is an edit to the
superseded ADR, which the first clause forbids. The repo has already resolved the tension in
practice, in the second direction:

`modules/MicroKit.Messaging/.claude-context/context/microkit-messaging-architectural-decisions.md:198`

```
## ADR-MSG-012: DomainEventDispatchBehavior — SUPERSEDED

**Status:** Superseded by ADR-MSG-013
```

ADR-MSG-012's *body* was edited after acceptance to record its supersession.

**Not resolved here, and the precedent deliberately not followed.** ADR-MSG-015 supersedes part of
ADR-MSG-014; ADR-MSG-014's body is left untouched, and the pointer was added to the decisions index
(`modules/MicroKit.Messaging/.claude/CLAUDE.md` → "Key Architectural Decisions") instead — the index
being a pointer rather than a decision record. That leaves ADR-MSG-012 and ADR-MSG-014 documenting
supersession by two different mechanisms. Someone should pick one and say so in the rule; this lot
had no mandate to.

Related: there is **no `decisions-index.md`** anywhere in the repo. The immutability rule lives at
the path above, and the module's de-facto index is the CLAUDE.md bullet list.

---

## Finding #6 — `OutboxMessage` has no correlation invariant, so the entity cannot keep its own promise

**Severity: low** (no reachable defect today).

`OutboxMessage.CorrelationId` is declared `= null!` with an XML doc that calls it non-nullable, and
the EF configuration marks it `IsRequired()`. Nothing enforces it in the type: it is a mutable
property on a public entity with no constructor and no factory gate.

Today nothing can reach a null. `OutboxMessageFactory.Create` already substitutes
`CorrelationId.New()` when the execution context carries none —

```csharp
CorrelationId correlationId =
    Guid.TryParse(context.CorrelationId, out var cGuid)
        ? CorrelationId.From(cGuid)
        : CorrelationId.New();
```

— and `OutboxMessageFactoryTests.Create_CorrelationId_WhenContextCorrelationIdIsNull_GeneratesNew`
has proven it since before this lot. So the write-side guarantee the L1 design proposed adding was
**already present**; there was nothing to fix.

What remains is that the guarantee lives in the factory, not the entity. The tracked debt in
`.claude-context/sessions/dette-microkit-messaging.md` anticipates consumers writing outbox rows
through their own non-EF writer (`NpgsqlOutboxWriter`); such a writer bypasses the factory entirely
and can persist a null. For that reason the dispatch-side read in `OutboxProcessor.DispatchAsync`
**keeps its null-conditional** (`message.CorrelationId?.Value.ToString()`) rather than adopting the
design's unguarded dereference. Making the entity enforce its own invariant — a constructor, or a
factory-only creation path — is the real fix and is out of scope.

---

## Finding #7 — `MediatROutboxDispatcher` now forecloses a raw-payload broker transport

**Severity: informational.** A deliberate trade-off, recorded so it is not rediscovered as a bug.

`MediatROutboxDispatcher` is a routing decorator over an inner `IOutboxDispatcher`. It previously
delegated to the inner dispatcher whenever `IMessageSerializer.Deserialize` returned null. It now
throws `OutboxPayloadException` instead, because a null return means the `EventType` resolved to no
CLR type or the JSON was malformed — both permanent, and previously misclassified as transient by
the inner dispatcher's bare `InvalidOperationException`, burning the full retry budget.

The trade-off: a future broker dispatcher that forwards raw payload bytes **without needing a CLR
type** would have been reachable through the old delegation and is not through the new throw. No
such dispatcher exists (v1 ships only `InProcessIntegrationDispatcher`, which needs the CLR type
too), and v2 broker packages are unwritten. If one is built, this decorator needs a passthrough mode
rather than a revert — the classification fix is correct independently.

---

## Finding #8 — `DeadLettered` is a stored derived fact

**Severity: low.** Untouched; flagged in the L1 design and confirmed.

`OutboxMessage.DeadLettered`'s own XML doc states it is always `true` when `Status` is
`Failed`. That makes it a derived fact stored as a column — two things that can disagree. Nothing in
the schema or the code prevents `Status = Failed, DeadLettered = false`, and every query that filters
on one but not the other is a latent inconsistency.

The rewrite writes both together in the same statement, so it cannot introduce a divergence. It also
did not remove the redundancy: dropping the column is a breaking schema change, and every consumer
query filtering on `DeadLettered` would need rewriting to `Status == Failed`. Worth an issue.

---

## Finding #9 — `ExecutionContext` stringly-types the correlation value objects

**Severity: low.** Untouched — changing it ripples well beyond the outbox.

`IExecutionContext` (in `MicroKit.Execution.Abstractions`) declares `CorrelationId` and `CausationId`
as `string?`, by design: ADR-EXEC-001 keeps the Level 0 contract free of any module's types. The
outbox therefore round-trips its own strong types through strings on every message:

```csharp
// write (OutboxMessageFactory)
Guid.TryParse(context.CorrelationId, out var g) ? CorrelationId.From(g) : CorrelationId.New()

// read (OutboxProcessor.DispatchAsync)
CorrelationId = message.CorrelationId?.Value.ToString(),
```

Two parses and two formats per message, and an unparseable string silently becomes a *new*
correlation chain rather than an error — which quietly breaks the trace it exists to preserve. The
Level 0 constraint is real, so the fix is not "use the VO in `IExecutionContext`"; it is probably a
typed adapter on the Messaging side. Out of scope.

---

## Finding #10 — the retention worker's first pass is an unbounded delete

**Severity: medium in production, zero in test.**

`OutboxRetentionWorker` issues one `ExecuteDeleteAsync` per pass with no chunking:

```csharp
await store.DeleteProcessedAsync(cutoff, tenantId: null, stoppingToken);
```

The brief specified "keep it simple — one pass on a slow timer", and that is what shipped. But the
**first** pass after this rewrite reaches an outbox that has never been cleaned, because
`DeleteProcessedAsync` had no caller before it. On a busy deployment that is a single `DELETE` over
potentially millions of rows: one long transaction, a table-level lock escalation risk on SQL Server,
and replication lag on PostgreSQL.

Not fixed, because chunking is a design decision (batch size, inter-batch delay, whether to bound by
row count or by time) that the brief scoped out. **Operators upgrading a long-running deployment
should run one bounded manual cleanup before enabling the worker**, or set `RetentionDays` high
initially and walk it down. A `RetentionBatchSize` option with a chunked delete loop is the real fix.

---

## Finding #11 — `ClaimToken` adds to the "consumer owns their own DDL" debt

**Severity: medium**, and it compounds an already-tracked blocker.

`.claude-context/sessions/dette-microkit-messaging.md` records that MicroKit ships no canonical SQL
for the outbox schema — the only definition lives inside `OutboxMessageConfiguration`, which a
consumer with their own DDL must reverse-engineer and then keep in sync by hand. Its own words: *"Two
sources of truth for one schema, drifting silently. That is precisely the failure mode an outbox
exists to prevent."*

This lot adds a column and two indexes to that undocumented schema:

```sql
ALTER TABLE <outbox> ADD COLUMN claim_token uuid NULL;
CREATE INDEX ix_outbox_claim_token   ON <outbox> (claim_token);
CREATE INDEX ix_outbox_dispatchable  ON <outbox> (dead_lettered, status, next_retry_at_utc, occurred_on_utc);
```

A consumer on EF migrations gets these automatically. A consumer who owns their DDL gets a runtime
failure on the first claim, with no migration note to consult because there is no migration document.

Two specific notes for whoever closes that debt:

- The design's DDL specifies a **partial** index (`WHERE claim_token IS NOT NULL`, and
  `WHERE dead_lettered = false`), which keeps both indexes small as published rows accumulate. The EF
  configuration ships **unfiltered** indexes instead, because `HasFilter` takes provider-specific SQL
  and `MicroKit.Messaging.EntityFrameworkCore` is the provider-neutral package. A consumer writing
  their own DDL should prefer the partial form.
- `ClaimToken` is deliberately mapped with **no value converter** — a plain `Guid?`. It is
  infrastructure, never a domain identifier, and giving it a strong type would buy nothing and cost a
  converter on the hottest write path in the module.
