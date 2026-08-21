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
