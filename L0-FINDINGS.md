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

**Status:** closed by the inbox claim rewrite (`feature/messaging/inbox-claim-rewrite`) as its
*headline purpose*, not as a side effect. `IInboxWriter.AddAsync` now returns
`InboxWriteResult.AlreadyPresent` instead of throwing, and `InProcessMessagePublisher` treats that
as a successful skip and continues to the next consumer — so the outbox marks the message
`Published`. The integration test that recorded this behaviour was **inverted, not deleted**: it is
now `InboxRedeliveryTests.Redelivery_AfterInboxRowsWritten_IsDeduplicatedAndTheOutboxRowIsPublished`,
and it is what stops the regression. See ADR-MSG-017.

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

---

# L2 Findings — domain-event sinks (ADR-MEDIATR-014 / -015)

Surfaced while implementing the sink model. Same standing as the findings above.

---

## Finding #12 — three negative assertions were unfalsifiable, and the defect class is invisible to the suite

**Severity: medium.** No production defect, but three tests reported coverage they did not provide,
and nothing in the repository can detect the next occurrence.

### The three assertions

`modules/MicroKit.Messaging/tests/MicroKit.Messaging.MediatR.UnitTests/DomainEventsDispatcherTests.cs`
— lines 32, 45 and 60, one in each of:

```
DispatchEventsAsync_WhenNoDomainEvents_WritesNoOutbox
DispatchEventsAsync_WhenDomainEventHasNoNotification_StillInvokesHandlerDispatcher
DispatchEventsAsync_WhenNotificationFactoryReturnsNull_SkipsOutbox
```

each asserting:

```csharp
await _outboxWriter.DidNotReceive().AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
```

The subject under test, `DomainEventsDispatcher`, writes to the outbox with **`AddBatchAsync`**, per
ADR-MSG-011. It never calls `AddAsync` on any path.

### Why that is not a typo

`DidNotReceive()` on a method the subject never invokes is green **no matter what the subject
does**. These three assertions could not fail. Each test's stated intent — "no outbox write
happened" — was checked against a method that is not the write path, so a real write would have
passed unnoticed. Rewriting the dispatcher to write on the empty-batch path, or to write twice, or
to write the wrong payload, would not have moved any of them.

That is the general shape, and it is the part worth remembering:

> A negative assertion is only as good as the method it names. `DidNotReceive().X()` where the
> subject uses `Y()` is indistinguishable, in every test run, from a correct assertion — and no
> mutation of the subject can expose it, because the assertion does not depend on the subject at
> all.

Positive assertions do not have this failure mode: `Received(1).AddAsync(...)` on a subject that
calls `AddBatchAsync` fails immediately and loudly. Only the negative form is silently inert, which
is why it needs reviewing by name rather than by eye.

### How long

For the entire life of the file as shipped. The dispatcher has used `AddBatchAsync` since
ADR-MSG-011 introduced the batch write, and these tests were written against it — so they were never
able to fail, including in `1.0.0-preview.4`. The same file's *positive* tests capture from
`AddBatchAsync` correctly, which is what makes the inconsistency visible on a careful read and
invisible on a casual one.

### Status

**Fixed in this lot**, as part of re-purposing the file. `DomainEventsDispatcherTests.cs` became
`OutboxDomainEventSinkTests.cs` when the glue's dispatcher became `OutboxDomainEventSink`
(ADR-MEDIATR-014), and every negative assertion now names `AddBatchAsync`. A new test,
`ReceiveAsync_WhenSomeEventsMap_WritesOnlyTheMappedOnesInOneBatch`, additionally pins the batch
contents positively, so the "wrote nothing" claim is no longer the only guard.

### Audit of the rest — not fixed, because nothing else is defective

Every `DidNotReceive()` / `DidNotReceiveWithAnyArgs()` in the four MicroKit.Messaging test projects
was checked against whether its subject invokes that method at all:

| File | Assertions | Verdict |
|---|---|---|
| `MicroKit.Messaging.MediatR.UnitTests/MediatROutboxDispatcherTests.cs` | 4 (`_inner.DispatchAsync`, `_publisher.Publish`) | **Sound** — the decorator calls both; each is the real branch being excluded |
| `MicroKit.Messaging.UnitTests/Publishing/InProcessMessagePublisherTests.cs` | 1 (`_inboxStore.AddAsync`) | **Sound** — `InProcessMessagePublisher.cs:83` calls exactly that |
| `MicroKit.Messaging.UnitTests/Processing/InboxProcessorTests.cs` | 8 (`MarkFailedAsync`, `DeadLetterAsync`, `MarkProcessedAsync`, `MarkProcessingAsync`) | **Sound** — all four are invoked by `InboxProcessor` |
| `MicroKit.Messaging.UnitTests/Processing/InboxProcessorTests.cs` | 4 (`ExistsAsync`, `AddAsync`) | **Sound, and deliberately so** — `InboxProcessor` never calls these *by design* (ADR-MSG-002: "pure drain … no `ExistsAsync`/`AddAsync` inside the loop"), and `ProcessBatch_DoesNotCallExistsAsync` is named for it. Unlike Finding #12 these are not "the wrong method for the intent"; the intent *is* that these specific methods stay uncalled, and the assertion fails the moment someone adds one |
| `MicroKit.Messaging.UnitTests/Processing/OutboxProcessorTests.cs` | 1 (`ApplyOutcomesAsync`) | **Sound** — the settlement call, invoked on every non-empty batch |
| `MicroKit.Messaging.UnitTests/Processing/OutboxRetentionWorkerTests.cs` | 1 (`DeleteProcessedAsync`) | **Sound** — the worker's only store call |

The discriminator that separates the last row of `InboxProcessorTests` from Finding #12 is worth
stating, because they look identical: a negative assertion naming an uncalled method is **correct**
when "this method must not be called" is the property under test, and **vacuous** when it is standing
in for "this *effect* did not happen" and the effect travels through a different method.

**Nothing here is being changed.** The count of genuine defects is three, all in the one file this
lot was already rewriting.

---

# Findings — inbox claim rewrite (`feature/messaging/inbox-claim-rewrite`)

Named for the branch rather than an ordinal: this lot is "L2" in the roadmap while `L2` above is
already spent on the domain-event sinks, and a heading that means two different things is worse
than no heading. Finding numbering continues unbroken.

Same standing as everything above: **nothing here was fixed**, and each is recorded precisely
because acting on it was out of scope for this lot.

---

## Finding #13 — retention deletes are unchunked in *both* workers, and the fix must land on both at once

**Severity: medium in production, zero in test.** This supersedes Finding #10's framing, which was
written when only the outbox had a retention worker and therefore described half the problem.

`OutboxRetentionWorker` and now `InboxRetentionWorker` each issue one `ExecuteDeleteAsync` per pass
with no chunking:

```csharp
await store.DeleteProcessedAsync(cutoff, tenantId: null, stoppingToken);
```

The **first** pass after an upgrade reaches a table that has never been cleaned, because
`DeleteProcessedAsync` had no caller before its worker existed — true of the outbox at L1 and true
of the inbox now. On a busy deployment that is a single `DELETE` over potentially millions of rows:
one long transaction, a table-level lock escalation risk on SQL Server, and replication lag on
PostgreSQL.

**Scoped out by decision, not by oversight.** The real fix is a `RetentionBatchSize` option plus a
chunked delete loop, and it has to land on **both** workers in the same change — `OutboxProcessorOptions`
and `InboxProcessorOptions`, `OutboxRetentionWorker` and `InboxRetentionWorker` — or the asymmetry
becomes the new defect. This lot could not do that without editing outbox files, and its
"zero outbox files touched" guarantee was worth more than closing a known, bounded, operator-visible
issue one side at a time.

The inbox is the milder of the two in one respect and the sharper in another. Milder: its 30-day
window plus the `Status == Processed` filter means the first pass reaches far less than the outbox's
never-cleaned table. Sharper: on the outbox an over-eager delete loses history, while on the inbox
it loses the deduplication guarantee outright — the table only deduplicates messages it still holds.

**Operators upgrading a long-running deployment** should run one bounded manual cleanup before
enabling either worker, or set `RetentionDays` high initially and walk it down.

---

## Finding #14 — `ReceivedAtUtc` is still stamped from the wall clock

**Severity: low.**

`InProcessMessagePublisher` sets `ReceivedAtUtc = DateTimeOffset.UtcNow` while everything around it
moved to an injected `TimeProvider`: `InboxProcessor`, `InboxRetentionWorker` and
`EfInboxStore<TContext>` all take one, and `AddMicroKitMessaging` already registers
`TimeProvider.System` via `TryAddSingleton`, so the dependency is free.

It matters more than a stray `UtcNow` usually would, because `ReceivedAtUtc` is the claim's ordering
key — `ClaimBatchAsync` selects candidates `OrderBy(m => m.ReceivedAtUtc)`. Today no test asserts
ingestion ordering, and the integration tests set the column directly, so nothing is untestable
right now; the point is that it *would* be if such a test were wanted.

**Not fixed, deliberately.** One fix, one problem: the design is silent on it, it is not one of the
ten defects this lot set out to close, and changing a constructor signature on the ingestion path to
tidy a clock reference is exactly the premature widening this repository's scope discipline exists
to prevent.

---

## Finding #15 — `IOutboxCoordinator`'s XML doc now describes an inbox that no longer exists

**Severity: low** (documentation, not runtime), **and it is a direct consequence of this lot.**

`modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/IOutboxCoordinator.cs` says:

> This supersedes the return-type mandate of ADR-MSG-014 for the outbox seam only; the inbox pair
> still returns `Task` until the inbox lot restores the symmetry.

The inbox lot has restored the symmetry. That sentence is now false: both inbox seams return
`ValueTask<InboxBatchResult>`.

**Not fixed, because the file is an outbox file** and this lot's scope guard was absolute — the
user reaffirmed "zero outbox files touched" as a guarantee mid-implementation. Correcting one XML
sentence would have been harmless in isolation and is exactly how an absolute guard erodes. The
decisions index in `.claude/CLAUDE.md` carries the accurate pointer (ADR-MSG-014 fully superseded as
a return-type mandate), so a reader following the ADR trail is not misled; only a reader of that one
file is. One line, in whichever lot next touches the outbox.

---

## Finding #16 — EF Core logs every deduplicated insert at `Error`, and a library cannot silence it

**Severity: low, but it will generate support questions.**

The module reports a redelivery at `Debug` and counts it as
`microkit.inbox.messages.deduplicated`, deliberately: under at-least-once delivery a redelivery is
normal operation, and one incident produces a burst of them. Warning-level would drown the log and
train whoever reads it to lower the level, losing the real warnings too.

EF Core does not cooperate. The rejected `INSERT` is logged by
`Microsoft.EntityFrameworkCore.Database.Command` and `Microsoft.EntityFrameworkCore.Update` at
`Error`, with a full stack trace, *before* the store absorbs it. So a healthy deployment producing a
steady trickle of redeliveries also produces a steady trickle of EF error logs about a condition
that was handled correctly.

The setting that would suppress it —
`optionsBuilder.ConfigureWarnings(w => w.Log((RelationalEventId.CommandError, LogLevel.Debug)))` or
similar — lives on the **consumer's** `DbContext`, which a library cannot configure. The same
constraint that ruled out the `EntityFramework.Exceptions` package (ADR-MSG-017, alternatives) rules
out fixing this from inside the module.

**Recorded rather than wished away, and asserted rather than hidden:**
`InboxRedeliveryTests` pins both halves — no warning from any `MicroKit.*` logger, and the
warnings that *are* present all come from `Microsoft.EntityFrameworkCore.*`. If a future change
makes the module itself noisy on the nominal path, that test fails.

A consumer who finds the noise unacceptable can downgrade `RelationalEventId.CommandError` on the
messaging `DbContext`. That belongs in consumer-facing documentation, which this lot did not add.

---

## Finding #17 — `AddEfCoreOutbox()` wires the inbox, and now wires six more registrations of it

**Severity: low** (naming, not behaviour), **but the gap widened in this lot.**

`MessagingBuilderExtensions.AddEfCoreOutbox<TContext>()` has always registered the inbox store as
well as the outbox one. After this lot it registers `EfInboxStore<TContext>` plus five interface
forwards — six of its eleven registrations are inbox — under a name that says outbox.

The consequence is not cosmetic. Both inbox workers log
*"Register `IInboxProcessorStore` (e.g. call `AddEfCoreOutbox()`)"* and
*"Register it (e.g. call `AddEfCoreOutbox()`)"* on a missing registration, which reads as a typo to
anyone who has not seen this file, and the obvious guess — `AddEfCoreInbox()` — does not exist.

**Not fixed:** renaming it to `AddEfCoreMessaging()` with an `[Obsolete]` alias is a public API
change on `MessagingBuilder`, which is api-reviewer territory and belongs in its own change rather
than riding along with a rewrite. Both packages are `1.0.0-preview.*` with zero external consumers,
so the rename could ship outright the way `AddMediatRTransport` → `AddMediatRDomainEvents` did.

---

## Finding #18 — EF Core's automatic savepoint makes the ingestion savepoint conditionally redundant

**Severity: informational.** Verified against EF Core 10.0.9, not assumed.

`EfInboxStore.AddAsync` creates an explicit savepoint before the insert when an ambient transaction
is present, so that a duplicate can be unwound without poisoning the caller's transaction —
necessary on PostgreSQL, where a constraint violation aborts the whole transaction and every later
statement fails, *including the verification query in the catch block itself*.

`DatabaseFacade.AutoSavepointsEnabled` defaults to `true`, and EF already creates and rolls back to
an automatic savepoint around `SaveChanges` inside a manually-started transaction. In the default
configuration the explicit savepoint is therefore belt and braces.

It was **kept**, on purpose: `AutoSavepointsEnabled` is a consumer setting, and a correctness
guarantee that holds only while a consumer leaves a flag alone is not a guarantee. The cost is one
extra round trip per ingestion under an ambient transaction, which is not the common path. The
interaction is documented on the method.

Worth knowing for whoever revisits this: nesting is harmless — rolling back to the outer savepoint
invalidates the inner one, which is ordinary SQL semantics.

---

## Finding #19 — the `MicroKit.Messaging.Testing` package is now further from existing

**Severity: low.**

`.claude/CLAUDE.md` and the naming and testing rules still describe a planned
`MicroKit.Messaging.Testing` package containing `InMemoryOutboxStore`, `InMemoryInboxStore` and
`FakeMessagePublisher`. The project does not exist, and the architecture tests carry a standing
`NOTE` to add it to `AllAssemblies_HaveNoMediatRContractsDependency` when it does.

This lot did not narrow that gap and slightly widened it: an `InMemoryInboxStore` would now have to
implement five interfaces rather than one, and — more awkwardly — `IInboxSettlementStore` is
defined by behaviour an in-memory double cannot honestly reproduce. `StageProcessedAsync` must stage
into *the handler's unit of work*; there is no unit of work in memory to stage into. The inbox tests
in this lot use a real isolated SQLite connection for exactly that reason.

**Not fixed.** Whoever builds that package should decide deliberately whether the inbox belongs in
it at all, rather than assuming symmetry with the outbox: a double that cannot reproduce the one
guarantee the contract exists to provide would assert the behaviour it was written to have.

---

## Finding #20 — `_rowIdsByKey` re-derives in hidden state what the claim already carries, and a per-tenant coordinator strands every lease through a `continue`

**Severity: low today, high the day a second topology exists.** Surfaced by the
distributed-context review of this lot.

`EfInboxStore` is the only store in the module holding mutable state —
`EfOutboxStore` holds none:

```csharp
private readonly Dictionary<InboxMessageKey, Guid> _rowIdsByKey = [];   // EfInboxStore.cs:51
```

`ClaimBatchAsync` populates it; `ApplyOutcomesAsync` reads it. That converts a value-passing
relationship into a **temporal** one, and the information is not new: `InboxClaim.Messages` already
carries every row, and `InboxMessage.RowId` is on each of them. The processor holds
`message.RowId` in hand while it builds the outcome.

**The trigger condition, stated plainly so the per-tenant lot cannot miss it.** ADR-MSG-002 defers
`PerTenantInboxCoordinator` to `MicroKit.Messaging.Multitenancy`, and that coordinator's natural
shape is a loop over tenants reusing the public `IInboxProcessor`. If two claims occur on one
scoped store instance before the first is settled —

```
claim(tenantA) → claim(tenantB) → settle(tenantA)
```

— then `ClaimBatchAsync` cleared the dictionary at the start of the second claim, `settle(tenantA)`
resolves **zero** keys, and every outcome hits this:

```csharp
if (!_rowIdsByKey.TryGetValue(outcome.Key, out var rowId))
{
    continue;                                        // EfInboxStore.cs:311-313
}
```

Zero rows written. Every lease in batch A strands for the full `LeaseDuration`. Nothing throws.

Two lesser variants: `Dictionary` is not thread-safe, so parallel claims on one instance corrupt
it (the `DbContext` would probably throw first, but the ordering is not guaranteed); and
`IInboxProcessorStore` *is* registered in the per-message execution scope too
(`MessagingBuilderExtensions.cs:53`), where its dictionary is empty — no caller does that today,
which is exactly what makes it a trap rather than a bug.

**Is it loud enough?** Partially, and misleadingly. `written < outcomes.Count` fires
`PartialSettlement` (event 2010, Warning), so it is not literally silent — but the message asserts
a cause that would be false: *"their lease either expired and was re-claimed, or their handler
already committed."* In the unresolved-key case the rows still carry the token; the store simply
could not map them. The one signal an operator has would send them to look at `LeaseDuration`.

**Not fixed here.** The fix is to carry `RowId` on `InboxOutcome` (or on `InboxMessageKey`) so
`ApplyOutcomesAsync` becomes pure and the field disappears — a public-contract change on
`MicroKit.Messaging.Abstractions`, which is api-reviewer territory and does not belong bolted onto
a review pass. Whoever writes the per-tenant coordinator must do this **first**, or document on
`IInboxProcessorStore` that `ApplyOutcomesAsync` must be called on the instance that produced the
claim with no intervening claim. That contract currently exists only in the implementation's head.

Two minor riders: `_rowIdsByKey.Clear()` sits at `EfInboxStore.cs:259`, reached only when
`claimedCount > 0`, so both empty-claim early returns leave the previous batch's mapping in place;
and `PartialSettlement` should distinguish "I could not map this outcome" from "the token moved
on".

### Amended after the api-reviewer pass — it is now a documented contradiction, not only a coupling

The API review found the same state asserted against on **permanent public surface**, in two
places that a third-party implementer reads and cannot get behind:

- `IInboxProcessorStore.ApplyOutcomesAsync` documents **one** cause for a short affected-row count
  — "the unwritten rows no longer carry this batch's token". The EF implementation has **two**: the
  documented one, and an outcome whose key is absent from `_rowIdsByKey`, which is `continue`d and
  never counted. An operator following the doc investigates `LeaseDuration` and finds nothing.
- `InboxClaim`'s remarks say the token "is carried explicitly rather than held as store state, so
  the store stays stateless". `EfInboxStore` **is** stateful across the two calls. The contract
  asserts the opposite of the reference implementation.

That raises the stakes on the fix. It is no longer only "a future topology would break": the
shipped contract currently tells an implementer something untrue about the only implementation
that exists, and both statements are on types that freeze at `1.0.0`. Whoever writes the
per-tenant coordinator should carry `RowId` on `InboxOutcome` and delete the field — which makes
both doc statements true rather than requiring them to be softened.

---

## Finding #21 — `ContextAwareServiceProvider` cannot influence constructor injection, so cascade outbox rows lose their tenant and correlation chain

**Severity: medium in production, and the shipped XML doc claims a guarantee that has never
held.** Pre-existing, identical on the outbox — **this lot introduces no regression**. It is
recorded here because the review that found it was commissioned for this lot, and because the
false doc is the part that makes it a finding rather than a note.

`PassThroughExecutionScope` wraps the scope's provider:

```csharp
public object? GetService(Type serviceType)
    => serviceType == typeof(IExecutionContext) ? context : inner.GetService(serviceType);
```

That intercepts a **direct** `GetService(typeof(IExecutionContext))` call made through the wrapper.
Nothing else. Every other resolution delegates to `inner`, and the inner `ServiceProvider` builds
the whole object graph from its own descriptors — a wrapper cannot reach into constructor
injection. So any service taking `IExecutionContext` as a constructor parameter receives the
scope's registered default instead:

```csharp
services.TryAddScoped<IExecutionContext>(
    _ => new Execution.ExecutionContext { CorrelationId = Guid.NewGuid().ToString() });
// TenantId = null, CausationId = null, CorrelationId = a fresh random Guid
```

No caller anywhere in the repo resolves `IExecutionContext` directly from a scope provider, so the
wrapper is effectively dead code. The sole production consumer, `OutboxDomainEventSink`, takes it
by constructor and hands it to `OutboxMessageFactory.Create`.

**Consequence, live on this branch.** An inbox handler that raises a domain event — MediatR
command → `TransactionBehavior` → dispatcher → `OutboxDomainEventSink` → `OutboxMessageFactory` —
writes a cascade outbox row with `TenantId = null` and a **brand-new random `CorrelationId`**. The
tenant of the inbox row is lost and the correlation chain is severed at exactly the hop this module
exists to preserve.

It is a **loss, not a bleed**: the default factory produces a fresh context per DI scope, so no
tenant A row can ever carry tenant B's identifier. Inbox handlers themselves are unaffected — they
read `TenantId` off the deserialized event. Only cascade writes are.

**The doc is the sharp end.** `PassThroughExecutionScopeFactory.cs:12-24` states that *"any scoped
service that depends on `IExecutionContext` … receives the message-row values (TenantId,
CorrelationId, CausationId) rather than the default fresh-Guid factory value"*, and follows it with
a **"Contract for custom implementations"** telling third parties to bridge the context the same
way. That instruction cannot be honoured by the mechanism it describes.

**Not fixed here**, because the fix is a change to shared execution infrastructure that both the
outbox and the inbox depend on, and it wants its own lot with the outbox's cascade path under test.
The shape: register a scoped `ExecutionContextHolder`, have `IExecutionContext` resolve from it,
and have `PassThroughExecutionScopeFactory` populate it on the new scope before returning — then
constructor injection works. Two riders for whoever takes it: correct the XML doc first, since a
false guarantee in shipped documentation is worse than a missing one; and note that
`TestExecutionScopeFactory` (`tests/MicroKit.Messaging.UnitTests/TestFixtures.cs:9-21`) **discards
the `IExecutionContext` parameter entirely**, so the test double is weaker than production and no
unit test can currently catch this.


---

## Finding #22 — the inbox metric names are alert contract, and the window to change them closes at 1.0.0

**Severity: low now, unfixable-in-practice later.** Surfaced by the api-reviewer pass on this lot.

`InboxMetrics` (`src/MicroKit.Messaging/Processing/InboxMetrics.cs`) publishes two instruments and
one tag key. All three are wrong against OpenTelemetry conventions in ways that are free to fix
today and expensive the moment anyone builds a dashboard on them.

**1. Two instruments where the convention wants one instrument and an attribute.**

```
microkit.inbox.messages.added          (:49)
microkit.inbox.messages.deduplicated   (:54)
```

These differ only by outcome. The type's own summary says *"the deduplication **rate** is the
metric worth alerting on"* — and splitting the outcome across two instruments is exactly what turns
that rate into a two-series join instead of one query with a filter. The conventional shape is a
single counter, `microkit.inbox.messages`, carrying the outcome as an attribute.

**2. The tag key squats on the namespace OpenTelemetry owns.**

`consumer.type` (`:64`) is unprefixed. The semantic conventions reserve unprefixed dotted attribute
names, and `messaging.*` already defines consumer attributes — `messaging.consumer.group.name`
among them. Either prefix it (`microkit.inbox.consumer.type`) or map onto the registered name.

**Why the window matters.** Instrument and attribute names are not source surface — nothing breaks
at compile time — which is exactly what makes them worse to change late. They become contract on
first subscription: the moment one consumer writes `AddMeter("MicroKit.Messaging.Inbox")` and
builds a dashboard, a panel or an alert rule on `microkit.inbox.messages.deduplicated`, renaming it
silently zeroes their alerting with no error anywhere. There is no obsoletion mechanism and no
compiler to catch it. **The packages are `1.0.0-preview.*` with no external consumers today, so the
change is free right now and effectively unavailable after `1.0.0` stable.**

**Not fixed in this lot**, which was scoped to the four merge blockers. It is a deliberate deferral
with a deadline, not an open question: the decision is already made, only the timing is open, and
the timing runs out at the first stable release.

---

## Finding #23 — the outbox settles a batch in a transaction of its own, so a notification handler's writes replay with it

**Severity: high on the MediatR path, none on the inbox path.** Surfaced by the XML-doc accuracy
pass, which was checking whether the claim *"widening the settlement window only costs
duplication"* is true. It is not.

### What the code does

`OutboxProcessor.ProcessBatchAsync` (`src/MicroKit.Messaging/Processing/OutboxProcessor.cs`) runs
the batch in two phases:

1. per message — a fresh `IExecutionScope`, `IOutboxDispatcher.DispatchAsync`, and the outcome
   **buffered in memory** (`outcomes.Add(OutboxOutcome.Published(...))`);
2. once, after the loop — `SettleAsync` → `IOutboxProcessorStore.ApplyOutcomesAsync` on the
   **batch-scoped** store, which opens its own transaction.

Whatever the dispatch target committed in phase 1 and the `Published` mark written in phase 2 are
therefore **two different transactions**, separated by the rest of the batch. A crash, a lease
expiry, or a settlement failure between them replays every message in the batch.

### Why that is fine on one path and not the other

| Dispatch target | Replay cost |
|---|---|
| `InProcessIntegrationDispatcher` → `IMessagePublisher` → **inbox rows** | Duplication only. The unique index on `(MessageId, ConsumerType)` absorbs the redelivery, `AddAsync` returns `AlreadyPresent`, and **no handler runs twice**. |
| `MediatROutboxDispatcher` → `IPublisher.Publish` → **`INotificationHandler`s** | **Handlers re-execute.** There is no per-consumer inbox on the notification path — the glue's own docs say so (ADR-MSG-009) — so a replay re-runs every handler for the message and re-writes whatever they wrote. A handler that projects into a read model produces its rows a second time. |

The second row is the default composition for anyone following the README: `AddInProcessTransport()`
then `AddMediatRDomainEvents()`. The idempotency contract is documented on
`AddMediatRDomainEvents`, `MediatROutboxDispatcher` and `OutboxDomainEventSink`, so a handler that
honours it is safe — but the contract is doing load-bearing work that the design could carry
instead, and it is stated as a retry contract, not as a batch-replay one.

### The asymmetry is the point

The consumer side already solved exactly this. `IInboxSettlementStore.StageProcessedAsync` stages
the processed mark into the **handler's own unit of work**, so the mark and the side effects commit
together or not at all — and `InboxMessageConfiguration` maps `ClaimToken` as a concurrency token so
a lost lease rolls both back. `IInboxSettlementStore`'s own remarks justify that design partly by
asserting the outbox does not need it "because that only widens duplication and an inbox
deduplicates downstream". That premise holds only where an inbox is downstream. On the MediatR path
nothing is.

### Shape of the fix — not taken here

The counterpart of `IInboxSettlementStore`: an outbox settlement store resolved from the
**per-message** execution scope that stages the `Published` mark into the transaction the dispatch
target is about to commit, with the batch-scoped `ApplyOutcomesAsync` reduced to the deferred cases
(retry, dead-letter, release, and success where the target committed nothing). The inbox's
`IsMarkUncommitted` fallback and its `HandlerDidNotCommit` warning transfer directly.

**Why it is not in this lot.** It changes `IOutboxProcessorStore` — a public contract — adds a
public interface, and requires the same PostgreSQL lease-expiry and concurrency tests the inbox lot
needed to prove the equivalent inbox guarantee. That is an implementation lot, not a documentation
pass. Two riders for whoever takes it: the outbox has **no** `ClaimToken` concurrency-token mapping
today (`OutboxMessageConfiguration` maps it as a plain `Guid?`), so the inbox's fencing mechanism
does not transfer for free; and the dispatcher is payload-agnostic by design, so the settlement seam
must not assume the target owns a `DbContext` — a broker dispatcher owns nothing to join.

**Documented, not fixed.** `OutboxProcessor`'s class remarks now carry this as a named known defect,
and the false "only costs duplication" premise has been removed from both `OutboxProcessor` and
`IInboxSettlementStore`.
