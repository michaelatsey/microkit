# Context: Architectural Decisions — MicroKit.Messaging

**ADR (Architecture Decision Records) for MicroKit.Messaging and MicroKit.Messaging.MediatR.**

Format: `## ADR-MSG-{NNN}: {Title}` · Status: `Accepted` | `Proposed` | `Superseded` | `Deprecated`

> ADR-MSG-001 through ADR-MSG-009 are documented in `.claude/rules/microkit-messaging-architecture.md`.
> This file contains ADRs created during the `fix/messaging/mediatr` branch.

---

## ADR-MSG-010: DomainEvent vs DomainEventNotification — Two Disjoint Types, Four Dispatch Phases

**Status:** Accepted  
**Date:** 2026-06-22  
**Amended by:** ADR-MSG-016 — the four phases stand; their *ownership* moved. `DomainEventsDispatcher` no longer exists and never superseded the core dispatcher. Read the Consequences section below through ADR-MSG-016.

### Decision

`IDomainEvent` and `IDomainEventNotification<TEventType>` are **structurally disjoint types** with
distinct dispatch semantics. A domain event is never a notification and a notification is never a
domain event. This disjointness drives a four-phase dispatch topology in `DomainEventsDispatcher`.

### Type definitions

```csharp
// Domain event — pure business fact raised by an aggregate
// Location: MicroKit.Domain.Events
public interface IDomainEvent : IEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

// Domain event notification — outbox payload wrapper for a domain event
// Location: MicroKit.MediatR.Abstractions
public interface IDomainEventNotification : INotification
{
    Guid Id { get; }  // stable message id — becomes OutboxMessage.MessageId
}

public interface IDomainEventNotification<out TEventType> : IDomainEventNotification
    where TEventType : IDomainEvent
{
    TEventType DomainEvent { get; }  // the original domain event embedded in the notification
}

// Concrete subclass pattern (one per event type, at most)
public sealed class OrderPurchasedDomainEventNotification
    : DomainEventNotification<OrderPurchasedDomainEvent>
{
    public OrderPurchasedDomainEventNotification(OrderPurchasedDomainEvent domainEvent)
        : base(domainEvent) { }
}
```

### Dispatch topology — four phases per `DispatchEventsAsync()` call

```
TransactionBehavior (order 700, commands only)
  → calls IDomainEventsDispatcher.DispatchEventsAsync() after command handler completes,
    before IUnitOfWork.CommitAsync()
      │
      ├── P1  IDomainEventsProvider.DrainDomainEvents()
      │        Drains all IDomainEvent instances accumulated on tracked aggregates.
      │
      ├── P2  IDomainEventHandlerDispatcher → IDomainEventHandler<TEvent>
      │        For EVERY drained event, invoke ALL registered handlers.
      │        Synchronous · in-transaction · receives raw IDomainEvent.
      │        All P2 handlers complete for all events before P3 begins.
      │        Use cases: read model projection, aggregate consistency, business effects.
      │
      ├── P3  IDomainEventNotificationFactory.Create(IDomainEvent)
      │        For each event, build the IDomainEventNotification<TEvent> wrapper.
      │        Returns null if no notification type is registered for this event type.
      │        Use cases: build outbox payload — logging, audit, Kafka, RabbitMQ, email.
      │
      └── P4  IOutboxWriter.AddBatchAsync(IReadOnlyList<OutboxMessage>)
               Write ALL notifications in one batch (single DB round-trip).
               Rows staged in EF Core change tracker — committed atomically
               with domain changes by TransactionBehavior's IUnitOfWork.CommitAsync.

DomainEventsCascadeNotificationPublisher (INotificationPublisher replacement)
  → called by MediatR after all INotificationHandler<T> handlers run for a notification.
    Calls IDomainEventsDispatcher.DispatchEventsAsync() to pick up cascade events raised
    by notification handlers (post-commit path, within the outbox processor scope).
```

### Why two foreach loops (not one)

All P2 handlers for ALL events complete before any P3/P4 notification work begins:

```csharp
// ✅ Two-foreach — P2 fully completes before P3/P4
foreach (var evt in domainEvents)
    await handlerDispatcher.DispatchAsync(evt, ct);     // P2

var outboxMessages = new List<OutboxMessage>(domainEvents.Count);
foreach (var evt in domainEvents)
{
    var notification = notificationFactory.Create(evt);  // P3
    if (notification is null) continue;
    outboxMessages.Add(outboxFactory.Create(...));
}
await outboxWriter.AddBatchAsync(outboxMessages, ct);    // P4

// ❌ Interleaved — P2 handler for event N could observe partial outbox state
foreach (var evt in domainEvents)
{
    await handlerDispatcher.DispatchAsync(evt, ct);      // P2
    var n = notificationFactory.Create(evt);             // P3
    if (n is not null) outboxMessages.Add(...);
}
await outboxWriter.AddBatchAsync(outboxMessages, ct);
```

The two-foreach pattern ensures:
1. P2 handler effects (aggregate mutations, read model updates) are fully applied before notifications are serialized as outbox payloads.
2. P2 handlers cannot observe a partially written outbox batch.
3. P4 is always a single batch — one DB round-trip regardless of event count.

### Handler registration — not related to notification registration

```
IDomainEventHandler<OrderPurchasedEvent>           ← P2 handler (zero or more per event)
DomainEventNotification<OrderPurchasedEvent>       ← P3 mapping (zero or one per event)
INotificationHandler<OrderPurchasedDomainEventNotification> ← post-commit MediatR handler
```

These three registrations are **independent**:
- An event can have P2 handlers without a notification mapping (pure in-transaction business effect).
- An event can have a notification mapping without P2 handlers (pure outbox fan-out).
- A notification can have zero, one, or many `INotificationHandler` implementations (MediatR fan-out).
- ADR-MEDIATR-005: exactly one `DomainEventNotification<TEvent>` subclass per event type.

### Consequences

- `DomainEventsDispatcher` in `MicroKit.Messaging.MediatR` is the authoritative implementation of
  `IDomainEventsDispatcher` when the Messaging glue is installed. It supersedes the basic
  `DomainEventDispatcher` in `MicroKit.MediatR` core (which only dispatches to P2 handlers, no outbox).
- `DispatchEventsAsync` is called in two places in normal operation:
  1. By `TransactionBehavior` (after command handler, before commit) — the primary dispatch path.
  2. By `DomainEventsCascadeNotificationPublisher` (after notification handlers) — the cascade path for post-commit events.
  Command handlers and notification handlers must never call `DispatchEventsAsync` directly.
- Notification handlers (`INotificationHandler<TNotification>`) are idempotent by contract —
  the outbox processor may retry and re-run all handlers (ADR-MSG-003 / ADR-MSG-009).

---

## ADR-MSG-011: IOutboxWriter.AddBatchAsync — Batch Outbox Write

**Status:** Accepted  
**Date:** 2026-06-22  
**Amended by:** ADR-MSG-016 — decision unchanged; the caller named `DomainEventsDispatcher` below is now `OutboxDomainEventSink`.

### Decision

`IOutboxWriter` exposes a second method `AddBatchAsync(IReadOnlyList<OutboxMessage>, CancellationToken)`
for writing multiple outbox rows in a single EF Core `AddRange` call.

### Rationale

`DomainEventsDispatcher.DispatchEventsAsync` may process N domain events in one command, each
producing one outbox row. Writing them one-by-one via N `AddAsync` calls adds N individual change
tracker interactions. `AddRange` stages all rows in one call.

This is purely a change-tracker optimization — `SaveChanges` still emits one INSERT per row (EF Core
does not batch INSERT statements by default unless `EnableSensitiveDataLogging` / PostgreSQL batch
insert is configured). The round-trip reduction is at the application layer (DbContext state), not
at the database layer.

### Implementation

```csharp
// IOutboxWriter (Abstractions)
ValueTask AddBatchAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default);

// EfOutboxStore (EntityFrameworkCore) — no SaveChanges, same pattern as AddAsync
public ValueTask AddBatchAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default)
{
    context.Set<OutboxMessage>().AddRange(messages);
    return ValueTask.CompletedTask;
}
```

`AddAsync` is kept unchanged — it remains the correct method for callers that write a single message
(e.g., integration-event-only publish paths not triggered by a domain event).

### Consequences

- All implementations of `IOutboxWriter` (including `InMemoryOutboxStore` in Testing) must implement
  `AddBatchAsync`. The default contract: if `messages` is empty, return immediately (no-op).
- `DomainEventsDispatcher` uses `AddBatchAsync` exclusively. The `AddAsync` path is no longer called
  from the glue dispatcher.

---

## ADR-MSG-012: DomainEventDispatchBehavior — SUPERSEDED

**Status:** Superseded by ADR-MSG-013  
**Date:** 2026-06-22  

`DomainEventDispatchBehavior` was proposed as a MediatR pipeline behavior (order 50) that would
call `IDomainEventsDispatcher.DispatchEventsAsync()` after the command handler. It was deleted
because `TransactionBehavior` (order 700, in `MicroKit.MediatR.Behaviors`) is the correct
behavior for this responsibility — it owns the transaction boundary and therefore owns the
dispatch+commit sequence. A separate outermost behavior at order 50 had no access to the
transaction and no relationship to the commit.

See ADR-MSG-013 for the accepted cascade dispatch design.

---

## ADR-MSG-013: DomainEventsCascadeNotificationPublisher — Cascade Event Dispatch on the Notification Path

**Status:** Accepted  
**Date:** 2026-06-22  
**Amended by:** ADR-MSG-016 — the publisher and its registration stand, under the new name `AddMediatRDomainEvents()`. The Consequences claim that installing the glue yields working cascade dispatch is **known false**: cascade rows are staged and never flushed. See ADR-MSG-016 §Known-false consequence.

### Context

Domain events must be dispatched at two points:
1. **After each command handling, before commit** — `TransactionBehavior` (order 700) calls
   `IDomainEventsDispatcher.DispatchEventsAsync()` then `IUnitOfWork.CommitAsync()`.
2. **After each domain event notification handling, without committing** — notification handlers
   (post-commit, outbox path) may themselves modify aggregates or call domain services that
   accumulate new domain events. Those cascade events need to be dispatched within the same
   outbox processor scope.

The challenge: MediatR's notification fan-out calls multiple `INotificationHandler<T>` instances
per notification. A per-handler decorator pattern would call `DispatchEventsAsync()` N times
(once per handler), and requires complex DI manipulation to register the decorator over every
handler discovered by MediatR's assembly scanning.

### Decision

Replace MediatR's default `ForeachAwaitPublisher` with `DomainEventsCascadeNotificationPublisher`
— a custom `INotificationPublisher` that:
1. Iterates all handler executors sequentially (same semantics as `ForeachAwaitPublisher`).
2. Calls `IDomainEventsDispatcher.DispatchEventsAsync()` **once after all handlers complete**.

This is the fan-out-aware adaptation of the notification-handler decorator pattern.

### Rationale

1. **One call, not N.** All handlers for a given notification fire before `DispatchEventsAsync()`.
   Cascade events from all handlers are collected and dispatched in a single pass, not one per handler.
2. **Single registration.** Replaces the singleton `ForeachAwaitPublisher` with a transient
   `DomainEventsCascadeNotificationPublisher`. No DI manipulation of individual handler descriptors.
3. **No-op on empty queue.** `DispatchEventsAsync()` returns immediately if no events were accumulated.
   The overhead when no cascade events exist is one call to `IDomainEventsProvider.DrainDomainEvents()`.
4. **Correct scope.** Registered as transient — the scoped `IDomainEventsDispatcher` is resolved
   from the outbox processor's per-message `IAsyncServiceScope` (ADR-MSG-002), not from the root.

### Registration

```csharp
// In AddMediatRTransport() — replaces MediatR's TryAddSingleton<INotificationPublisher, ForeachAwaitPublisher>
builder.Services.Replace(
    ServiceDescriptor.Transient<INotificationPublisher, DomainEventsCascadeNotificationPublisher>());
```

### Consequences

- Consumers who install `AddMediatRTransport()` automatically get cascade dispatch on the
  notification path.
- Consumers who use only `AddMicroKitMediatR()` (no Messaging glue) keep MediatR's default
  `ForeachAwaitPublisher` — no cascade dispatch on notifications (only TransactionBehavior dispatch).
- Notification handlers remain idempotent by contract — the outbox processor may retry a
  notification and re-run all handlers (ADR-MSG-003 / ADR-MSG-009).
- `DomainEventsCascadeNotificationPublisher` is `internal sealed` — only architecture tests in
  the Messaging.MediatR test project need to reference it.

---

## ADR-MSG-014: Task (not ValueTask) on Coordinator and Processor Interfaces

**Status:** Accepted
**Date:** 2026-06-22

### Context

MicroKit convention requires `ValueTask` for all async methods (CLAUDE.md rule #9). The four
public interfaces `IOutboxCoordinator`, `IInboxCoordinator`, `IOutboxProcessor`, and
`IInboxProcessor` return `Task` instead.

### Decision

These four interfaces return `Task` as an explicit, documented exception to the ValueTask rule.

### Rationale

1. **BackgroundService chain.** `BackgroundService.ExecuteAsync()` is `Task`-based. The call chain
   `OutboxWorker.ExecuteAsync` → `IOutboxCoordinator.ExecuteAsync` → `IOutboxProcessor.ProcessBatchAsync`
   is always `await`-ed from a `Task`-returning context. Returning `ValueTask` at the coordinator or
   processor level would require `.AsTask()` adapters at each call site, adding allocation overhead
   with zero benefit — the ValueTask boxing optimization applies when the result is frequently
   synchronously available, which is never the case here (each call performs I/O).
2. **Symmetry.** All four seam interfaces (two coordinators, two processors) share the same return
   type. A `Task`/`ValueTask` mix within the same call chain would be confusing without benefit.
3. **Negligible allocation concern.** These methods execute once per polling cycle (~5s default
   interval) and drive I/O-bound work. The per-call allocation difference between `Task` and
   `ValueTask` is irrelevant at this cadence.

### Scope of exception

- `IOutboxCoordinator.ExecuteAsync(CancellationToken)` → `Task`
- `IInboxCoordinator.ExecuteAsync(CancellationToken)` → `Task`
- `IOutboxProcessor.ProcessBatchAsync(int, CancellationToken)` → `Task`
- `IInboxProcessor.ProcessBatchAsync(int, CancellationToken)` → `Task`

All other public async methods in Abstractions and Core return `ValueTask` as required.

### Consequences

- The api-reviewer checklist notes this exception explicitly; CLAUDE.md rule #9 carries a one-line
  carve-out referencing this ADR.
- XML docs on each interface already document the reason (BackgroundService chain compatibility).
- Future coordinator/processor-pattern interfaces in this chain should default to `Task`.

---

## ADR-MSG-015: ValueTask&lt;OutboxBatchResult&gt; on the Outbox Coordinator and Processor

**Status:** Accepted
**Date:** 2026-08-21
**Supersedes (in part):** ADR-MSG-014 — the return-type mandate only, and only its two outbox lines.

### Context

ADR-MSG-014 mandated `Task` for the four coordinator/processor seam interfaces. Two things have
changed since.

First, `ProcessBatchAsync` now produces a value. The outbox rewrite makes a batch report what it
achieved — claimed, published, retried, dead-lettered, released, and why it stopped early. The
hosting worker needs that to set its own cadence: poll again immediately when a batch comes back
full, back off geometrically while the queue is idle, back off hard while the transport is down. A
`Task` discards it, which leaves the worker on a fixed timer — one of the seven defects this lot
fixes. A result that the type system throws away is not a result.

Second, and this matters for how the next reader should weigh ADR-MSG-014: **its rationale #1 was
factually wrong.** It argued that returning `ValueTask` here "would require `.AsTask()` adapters at
each call site, adding allocation overhead with zero benefit". Awaiting a `ValueTask` from a
`Task`-returning context requires no adapter and allocates nothing; `BackgroundService.ExecuteAsync`
awaits a `ValueTask` exactly as happily as a `Task`. This ADR therefore supersedes on a *mistaken
premise*, not on a correct-but-outdated one. An ADR overtaken by events and an ADR whose reasoning
did not hold are different things, and the distinction is worth recording.

Rationale #3 (allocation is irrelevant at polling cadence) remains true, and is not the ground for
this change. The ground is that the operation now has a return value.

### Decision

`IOutboxCoordinator.ExecuteAsync` and `IOutboxProcessor.ProcessBatchAsync` return
`ValueTask<OutboxBatchResult>`.

`ValueTask` rather than `Task<OutboxBatchResult>` because `ValueTask` is the MicroKit convention for
async library code (root CLAUDE.md, module rule #9). ADR-MSG-014 was an explicit carve-out from that
convention; with its stated justification removed, the convention applies again.

### Scope of supersession

Superseded — these two lines of ADR-MSG-014's "Scope of exception":

- `IOutboxCoordinator.ExecuteAsync(CancellationToken)` → now `ValueTask<OutboxBatchResult>`
- `IOutboxProcessor.ProcessBatchAsync(int, CancellationToken)` → now `ValueTask<OutboxBatchResult>`

Still in force — the inbox half, unchanged:

- `IInboxCoordinator.ExecuteAsync(CancellationToken)` → `Task`
- `IInboxProcessor.ProcessBatchAsync(int, CancellationToken)` → `Task`

Untouched — everything else in ADR-MSG-014 and in ADR-MSG-002: the Worker / Coordinator / Processor
decomposition, the roles of each, `internal sealed` implementations, and the public seam interfaces
that let a deferred per-tenant coordinator compose the engine rather than reimplement it. Only the
return-type mandate is affected.

### Consequences

- **The symmetry ADR-MSG-014 protected is deliberately broken, and the break is temporary.** The
  inbox pair stays on `Task` for exactly one reason: the inbox rewrite is a separate lot, and
  changing its signature here would be a breaking change unaccompanied by the batching work that
  justifies it. **The inbox lot is expected to restore symmetry by moving the inbox seam to the same
  shape** — a result type plus `ValueTask`. Until it does, the two halves differ. An asymmetry that
  is recorded and dated is debt; a silent one is drift, and this file exists to keep it the former.
- Consumers implementing `IOutboxCoordinator` or `IOutboxProcessor` must update their signatures.
  This is a breaking change on `MicroKit.Messaging.Abstractions`, sequenced with the outbox rewrite
  release rather than separately.
- `OutboxWorker` derives its polling interval from the returned result instead of a fixed timer.
- The api-reviewer checklist and module CLAUDE.md rule #9 carve-out must name this ADR for the outbox
  seam and keep naming ADR-MSG-014 for the inbox seam.

### Alternatives considered

**Keep `Task` and expose the batch result through a separate channel** — an event on the coordinator,
a callback passed into `ExecuteAsync`, or mutable state read by the worker after the call.
**Rejected.** It hides in a side channel a value that is the direct return of the operation. Every
variant is worse than the signature change it avoids: an event inverts control for a value that is
already synchronously available to the caller; a callback puts the worker's cadence policy inside the
processor's call stack; mutable state on a scoped coordinator is a data race waiting for the first
consumer who resolves it twice. None of them removes the breaking change either — they just move it
somewhere less visible.

**Return `Task<OutboxBatchResult>`** — preserves ADR-MSG-014's symmetry argument at the cost of the
`ValueTask` convention. Rejected because the symmetry is broken by the inbox lag regardless, and
between two conventions in tension the one with a live justification wins.

---

## ADR-MSG-016: The Glue Contributes a Sink — Messaging Half of ADR-MEDIATR-014/-015

**Status:** Accepted
**Date:** 2026-08-22
**Amends:** ADR-MSG-010 (phase ownership), ADR-MSG-011 (caller name), ADR-MSG-013 (registration name, and one consequence that was never true).
**Requires:** MicroKit.MediatR from the same release — the glue does not compile against a core without `IDomainEventsSink`.

### Context

ADR-MSG-010 gave `MicroKit.Messaging.MediatR` a type called `DomainEventsDispatcher` that
implemented `IDomainEventsDispatcher` and ran all four phases: drain (P1), synchronous
`IDomainEventHandler<TEvent>` dispatch (P2), notification mapping (P3), outbox batch write (P4).
`MicroKit.MediatR` core shipped its own `DomainEventDispatcher` running P1 and P2. Both claimed the
same DI slot, and ADR-MSG-010's Consequences declared the glue's copy "authoritative" — it
"supersedes the basic `DomainEventDispatcher` in MicroKit.MediatR core".

That arrangement had two participants racing for one slot, arbitrated by registration order, with
P1 and P2 implemented twice and free to drift. ADR-MEDIATR-013 tried to fix it with a
`TryAdd`/`Replace` precedence contract — correctness resting on two packages honouring a mutual
convention that nothing verified. ADR-MEDIATR-014 replaced the arbitration with composition, and
ADR-MEDIATR-015 fixed the two naming and registration defects that survived it. This ADR records
what those decisions mean on the Messaging side, so this file stops contradicting the code it
governs.

### Decision

1. **The glue registers no `IDomainEventsDispatcher`.** There is exactly one implementation of that
   interface and it lives in `MicroKit.MediatR` core. The Consequences of ADR-MSG-010 are inverted:
   the core dispatcher is authoritative and the glue does not supersede it.

2. **`DomainEventsDispatcher` becomes `OutboxDomainEventSink`, an `IDomainEventsSink`.** It sheds P1
   and P2 — which it duplicated from core — and keeps P3 and P4. The core orchestrator drains, runs
   the whole P2 pass for the batch, and only then hands the batch to each sink. The barrier between
   P2 and the sinks is load-bearing: a P2 handler can never observe a partially written outbox
   batch.

3. **Contributed with `TryAddEnumerable`, never `Add`.** `TryAddEnumerable` dedups on
   `(ServiceType, ImplementationType)`, so calling the registration twice contributes one sink.
   Under a plain `Add` the second copy would receive every batch and write every outbox row twice.
   Because Microsoft DI resolves `IEnumerable<T>` to every registration for `T`, the composition is
   order-independent by construction: it no longer matters whether `AddMicroKitMediatR()` ran before
   or after the glue.

4. **`AddMediatRTransport()` is renamed `AddMediatRDomainEvents()`, outright, with no `[Obsolete]`
   alias.** The old name was wrong twice: the method transports nothing, and
   `Add{Provider}Transport()` is reserved by this module's naming rules for broker providers. Both
   packages are `1.0.0-preview.*` with zero external consumers and one in-repo call site — this is
   the cheapest the rename will ever be.

5. **`AddInProcessTransport()` registers all three of its services with `TryAdd`.** A transport
   supplies a default and abstains when something already holds the slot.

6. **`AddMediatRDomainEvents()` makes four registrations, not three** — the sink, the
   `IOutboxDispatcher` decoration, the `INotificationPublisher` replacement, and a `TryAdd`ed
   `IMessageSerializer` default. The fourth is not redundant: the decorator takes a serializer as a
   constructor dependency, and the method's precondition proves only that an `IOutboxDispatcher`
   exists, not that whoever registered it registered a serializer too.

### Rationale

1. **A race removed beats a race arbitrated.** ADR-MEDIATR-013's precedence contract made the
   *documented* order correct; it did not make a wrong order fail loudly. With sinks there is
   nothing to select, so there is no order to get wrong.

2. **Contribution scales; replacement does not.** The `Replace` shape has a ceiling of two
   participants — a second package wanting in-transaction participation would have to know about
   the first and re-implement its sequence to append to it. An `IEnumerable<IDomainEventsSink>` has
   no ceiling and requires no participant to know about any other.

3. **One implementation of P1/P2 cannot drift from itself.** The duplicated drain and handler pass
   were two copies of one algorithm in two repositories' worth of review surface.

4. **`TryAdd` on the transport closes a silent failure, not a theoretical one.** Under the previous
   plain `Add`, calling `AddInProcessTransport()` *after* the glue appended a second
   `IOutboxDispatcher` descriptor; Microsoft DI resolves the last registration, so the decorator was
   bypassed with no exception and no log. The outbox kept draining and nothing it routed was ever
   published. This is the failure mode the composition rules exist to prevent, and it had shipped.

5. **The rename is a naming-rule consequence, not a preference.** `microkit-messaging-naming.md`
   reserves `Add{Provider}Transport()` for brokers. A method that contributes a sink, decorates a
   dispatcher, replaces a publisher and supplies a serializer default is not a transport by any
   reading.

### Consequences

- **No consumer-visible type changed.** `DomainEventsDispatcher` and `OutboxDomainEventSink` are
  both `internal sealed`. The package's public surface remains one type
  (`MessagingMediatRExtensions`) and one method.
- **The rename is a breaking change on a preview package** and is recorded as such in the CHANGELOG.
  Migration is renaming the call: same receiver, same signature, same position in the chain.
- **`AddInProcessTransport()` now abstains instead of imposing, in both directions.** A consumer who
  registered their own `IMessagePublisher` first now keeps it, where previously it was silently
  displaced — a runtime change, not DI bookkeeping. A consumer calling it *after* a broker transport
  no longer overrides that broker. Neither direction is likely today (all broker providers are
  `IsPackable=false`), but both are behavioural.
- **The one remaining ordering requirement is that a transport precede
  `AddMediatRDomainEvents()`**, because the decoration needs something to wrap. Getting it wrong
  throws at startup naming the fix, rather than failing silently.
- **`MicroKit.MediatR` and `MicroKit.Messaging.MediatR` must ship from the same release.** The two
  halves of ADR-MEDIATR-014 are not independently versionable.
- **ADR-MSG-011 is unaffected in substance**: `AddBatchAsync` is still the exclusive write path, now
  called by `OutboxDomainEventSink` rather than by `DomainEventsDispatcher`.

### Known-false consequence inherited from ADR-MSG-013

ADR-MSG-013 states that consumers who install the glue "automatically get cascade dispatch on the
notification path". **That is not true and this ADR does not make it true.** The cascade dispatch
itself works — `DomainEventsCascadeNotificationPublisher` calls the core dispatcher after all
handlers, the drain happens, and the sink produces the outbox row, which
`DomainEventSinkStagingTests.CascadePublish_WhenHandlerRaisesEvent_StagesOutboxRowInProcessorScope`
pins against the change tracker. But nothing on the outbox processing path calls `SaveChanges`:
`TransactionBehavior` is the sole flush owner and is not in that path, so the staged row dies with
the processor scope. The cascade domain event is lost with no row, no exception, and nothing logged
at Warning or above — recorded by `CascadeObservationTests`, which asserts the loss rather than a
desired outcome, and disclosed to consumers in the module README.

This is pre-existing, out of scope for the sink refactor, and tracked separately. It is named here
because ADR-MSG-013's Consequences section will otherwise keep asserting a guarantee the test suite
disproves. **Do not resolve it by deleting the observation test.** Before 1.0.0 stable one of three
things must happen: the outbox path acquires a flush owner, the cascade publisher fails loudly, or
cascade dispatch leaves the stable surface.

### Alternatives considered

**Finish the ADR-MEDIATR-013 `Replace` repair** — one line plus a test, and it does fix the
order-dependence for that one service type. Rejected: it buys order-independence at the price of
permanently ratifying the shape that made order matter, leaves P1/P2 duplicated and drifting, keeps
the two-participant ceiling, and leaves correctness resting on a mutual contract nothing verifies.

**Decorator chain — the glue decorates the core `IDomainEventsDispatcher`.** Rejected: it removes
the duplication but not the ordering problem, since which decorator ends up outermost is still
positional, and every decorator must reconstruct the descriptor beneath it. The cost of that
reconstruction is already visible in this package's `IOutboxDispatcher` decoration
(`LastOrDefault` + `Remove` + a three-branch `CreateInner`). At three participants it is worse than
what it replaces.

**Keep `AddMediatRTransport()` and add an `[Obsolete]` alias.** Rejected: it preserves the exact
name whose defect is that it misdescribes the method, and commits to carrying it past 1.0.0. Zero
external consumers and one in-repo call site make outright rename strictly cheaper now than it will
ever be again.

**Leave `AddInProcessTransport()` on plain `Add` and document the required call order.** Rejected:
it trades a DI-ordering bug for a documentation-ordering bug, and the failure mode it leaves in
place is silent.

---

## ADR-MSG-017: Inbox Claim, Settlement Inside the Handler Transaction, and Ingestion That Returns

**Status:** Accepted
**Date:** 2026-08-23
**Supersedes (in part):** ADR-MSG-014 — its two remaining inbox return-type lines, which ADR-MSG-015 explicitly left to "the inbox lot".
**Amends:** ADR-MSG-003 (the inbox delivery guarantee is narrowed, not replaced), ADR-MSG-006 (the inbox store splits the way the outbox store already did).

### Context

The inbox is the system's deduplication mechanism, and it did not deduplicate safely.

`MarkProcessingAsync` carried no eligibility predicate and returned `void`: an unconditional
write, not a lease. Two processors could both "acquire" the same row, both invoke the handler,
and neither could tell. The three terminal writes filtered on the compound key alone, so a
processor whose lease had expired overwrote the one that legitimately re-claimed the row, and all
three returned `Result.Success()` regardless of affected rows. Handler resolution sat outside the
`try`, so one unregistered handler killed the whole batch and stranded every lease until expiry.
Two structurally permanent failures — an unregistered `ConsumerType`, a payload that will not
deserialize — each burned the full retry budget on a verdict fixed at the first attempt. A
downstream outage failed all N rows, wrote N failure rows, consumed N retry budgets, and repeated
next tick.

Worst, because it corrupted a path that was otherwise working: `AddAsync` reported its **nominal**
outcome — "already present" — by throwing. Under at-least-once delivery a redelivery needs no
failure at all; one expired lease after a crash produces it. Nothing caught the exception, so it
reached `OutboxProcessor`, was classified a transient dispatch failure, and retried into the same
duplicate until the message dead-lettered. A message delivered correctly on the first attempt was
destroyed by the mechanism meant to protect it. The end-to-end harness recorded this as observed
behaviour before the fix existed.

### Decision

1. **The claim replaces the per-message lease.** `ClaimBatchAsync` reserves up to `batchSize` rows
   in one `UPDATE` that replays the eligibility predicate inside itself, stamping a lease and an
   ownership token. `ApplyOutcomesAsync` settles the batch, and **every write filters on the
   token**, so a processor whose lease expired matches zero rows instead of overwriting its
   successor. Round trips per batch drop from `2N+1` to three, plus two only when something failed.

2. **The primary key moves to a `RowId` surrogate; the compound key survives as a unique index.**
   Not decoration. Filtering an `UPDATE` with two `Contains` over a compound key selects the cross
   product of both lists rather than the candidate pairs: 20 candidates spanning 5 consumers could
   claim up to 100 rows, so "at most `batchSize`" was not merely imprecise but violated. The
   surrogate collapses the claim to one exact, bounded list — `WHERE "RowId" = ANY (@ids)`, one
   bound array parameter, plan-cacheable at any batch size. The dedup gate is unchanged: the
   unique index on `(MessageId, ConsumerType)` is still the sole authority.

3. **Success settles inside the handler's own transaction; failures are batched.** This is the
   decision the rest exists to serve, and it is why the inbox is **not** a mirror of the outbox.
   An outbox may widen its crash window from one message to one batch, because that widens only
   duplication and the inbox absorbs it downstream. For the inbox there is no downstream — the
   inbox *is* the absorber. A crash between a handler returning and its row being marked reruns
   the handler with its business side effects, so batching that settlement would turn one possible
   replay into N. `IInboxSettlementStore.StageProcessedAsync` therefore uses the tracked change
   pipeline rather than `ExecuteUpdateAsync`, precisely because it must **not** execute
   immediately. A failed handler rolled back and has nothing to join, so failures batch freely.

   **The staged read states `AsTracking()` explicitly, and that is load-bearing.** It is the one
   query in `EfInboxStore` that requires tracking; the other four all say `AsNoTracking()`. A
   consumer whose `DbContext` sets `UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)` —
   an ordinary setting on a read-heavy application, and this store runs on *their* context — would
   otherwise get an untracked row whose mutations reach nothing. The guarantee must not depend on
   a setting the library cannot control: the same reasoning that rejected `EntityFramework.Exceptions`
   for the dedup gate.

4. **`ClaimToken` is mapped as an EF concurrency token.** Filtering the read on the token proves
   ownership at read time only; the `UPDATE` that `SaveChanges` emits later would carry the
   primary key alone, and a lease that expired in between would be silently overwritten — exactly
   the race the token exists to prevent. With the mapping, the token lands in the `WHERE` clause,
   a lost lease surfaces as `DbUpdateConcurrencyException`, and the handler's whole transaction
   rolls back — business side effects and mark together. Removing the mapping breaks no test that
   does not exercise concurrency, which is why it is configured centrally and pinned by a
   PostgreSQL test rather than left to consumers.

5. **The token doubles as the "already settled" marker.** A committed handler transaction leaves
   it null, so every deferred write for that row — including a `Released` issued while the host is
   shutting down — filters to zero rows and becomes a no-op. Nothing has to check for that case;
   it is structurally impossible. This is an invariant, not a happy accident.

6. **Ingestion returns `InboxWriteResult` instead of throwing.** The signature change is not a
   cost of the fix; it *is* the fix. A method whose expected result can only be expressed as an
   exception has the wrong signature. `InProcessMessagePublisher` treats `AlreadyPresent` as a
   successful skip and **continues to the next consumer** — that `continue` is the whole repair:
   the publisher returns normally, so the outbox marks the message `Published` instead of retrying
   it to death, and consumers after a duplicated one still get their row.

7. **The duplicate is recognised by post-hoc verification, in the store.** After a failed insert
   the row is queried: if it is there now, the gate held. That asks the question the decision
   actually depends on — "is the message recorded?" — rather than the syntactic one a detector
   answers, "was that error a unique violation?". It is a check *after* the insert, never a guard
   before it, so the unique index remains the sole authority and there is no
   time-of-check-to-time-of-use window. It lives in the store because the store owns the index;
   catching it in Core would force a provider-neutral package to decode `SqlState`, and every
   future ingestion path would repeat the catch.

8. **Failures are classified, and only proven permanence dead-letters.** An unregistered consumer
   and an unreadable payload raise `InboxPayloadException` and dead-letter on **first sight**.
   `InboxDependencyUnavailableException` abandons the batch and releases the remainder as
   `Released`, consuming no retry budget. `InboxConfigurationException` settles the batch — every
   row released — and only then rethrows, so the worker stops without stranding a lease.
   Everything unrecognised stays transient: a library that guesses permanence wrongly loses
   messages.

9. **Back-off gains full jitter, matching the outbox.** `Uniform(0, min(2^RetryCount s,
   MaxRetryBackoff))`, computed by the processor rather than the store, with `TimeProvider` and
   `Random` injected so the curve is asserted against exact values without a database.

10. **`IsLeaseLost` lives on the settlement store.** A lost lease and an ordinary domain
    concurrency conflict surface from the same `SaveChanges` as the same exception type. The
    discrimination is structural — which entity failed — and is a persistence-technology question,
    so it belongs to the implementation that knows the provider's exception types. Putting it
    there is also what keeps `MicroKit.Messaging` free of any EF Core reference, which its
    architecture tests enforce.

### Scope of supersession

Superseded — the two remaining lines of ADR-MSG-014's "Scope of exception":

- `IInboxCoordinator.ExecuteAsync(CancellationToken)` → now `ValueTask<InboxBatchResult>`
- `IInboxProcessor.ProcessBatchAsync(int, CancellationToken)` → now `ValueTask<InboxBatchResult>`

ADR-MSG-014 now has no operative return-type mandate left: ADR-MSG-015 took the outbox pair, this
takes the inbox pair, and the asymmetry ADR-MSG-015 recorded as dated debt is closed. Its body is
left untouched, following the ADR-MSG-014 ← ADR-MSG-015 precedent rather than ADR-MSG-012's
body-edit form; the pointer lives in the decisions index in `.claude/CLAUDE.md`.

Untouched — everything else in ADR-MSG-014 and in ADR-MSG-002. The Worker / Coordinator / Processor
decomposition, the shared-database default topology and the deferred per-tenant coordinator are
unchanged, and `SharedDbInboxCoordinator` now has the architecture test guarding that seam that its
outbox twin already had.

### Consequences

- **Delivery is transactionally atomic for database-backed handlers, and that phrasing is
  deliberate.** The processed marker and any database side effects written through the scope's
  `DbContext` commit together or not at all. It is **not** exactly-once in general: a handler that
  calls an external endpoint and then rolls back calls it again on replay, so database effects
  happen effectively once and external effects at least once. Stating this in consumer-facing docs
  rather than hiding it is part of the decision.
- **A handler that commits no unit of work is detected, not degraded silently.** The staged mark
  would otherwise never persist, and the row would replay on every pass and eventually dead-letter
  although every invocation succeeded. `IsMarkUncommitted` catches it after the handler returns,
  the processor falls back to a deferred `Processed` outcome, and it warns that the transactional
  guarantee did not apply for that row.
- **A handler that commits and then throws is a post-commit fault: counted processed, not
  retried.** The work is durable. Retrying would be wrong in principle and a no-op in practice —
  the committed transaction cleared the token, so the retry write would match zero rows and emit a
  misleading "lease expired" warning about a nominal path. This mirrors the rule the command
  pipeline already applies.
- **The scope-identity hazard is signalled, and this ADR should say which parts.** If the
  settlement store and the handler resolve different `TContext` instances, the mark never rides
  the handler's transaction. Three cases, and they are not equally visible:
  - **`TContext` not registered `Scoped`** (e.g. `ServiceLifetime.Transient`), or a handler that
    writes through a second `DbContext`: the staged entry stays `Modified` on the store's context,
    `IsMarkUncommitted` returns true, and the processor emits `HandlerDidNotCommit` (event 2009,
    Warning) plus a deferred `Processed` outcome. **Loud, and correct.**
  - **The context defaults to `NoTracking`**: was **silent** — no entry existed, so
    `IsMarkUncommitted` reported the mark committed, the batch reported `Processed`, and the row
    replayed forever without incrementing `RetryCount`. Closed by the explicit `AsTracking()` in
    Decision 3, and pinned by
    `InboxSettlementGuaranteeTests.StageProcessedAsync_PersistsTheMark_WhenTheContextDefaultsToNoTracking`.
  - **The staged mark discarded** (`ChangeTracker.Clear()` in a batch-processing handler): was
    **silent** for the same reason. `IsMarkUncommitted` now treats an absent or detached entry as
    *uncommitted* — absent means unknown, and unknown must fail toward a redundant deferred write
    rather than toward a lost mark. Pinned by
    `IsMarkUncommitted_ReportsUncommitted_WhenTheStagedMarkWasDiscarded`.

  Both silent cases were found in review and confirmed by observation before being fixed. Stating
  which hazards are handled matters: an ADR that only warns invites someone to re-fix a covered
  case while an uncovered one goes unmentioned.
- **The scope-identity guarantee is asserted against a composed container, not a comment.**
  `InboxSettlementGuaranteeTests` resolves `IInboxSettlementStore` and the handler's `TContext`
  from a real `IExecutionScope` and proves the handler's own `SaveChanges` persists the staged
  mark. Every other inbox test builds the store directly with an explicit context, which proves
  store behaviour and nothing about DI. The guarantee is a composition property, so it needed a
  composition-level test — and that test is what would have caught the `NoTracking` defect.
- **`InboxBatchResult.LeasesLost` is the operational signal for `LeaseDuration`,** the inbox's
  most consequential setting. It must exceed the worst-case handler duration, and without the
  counter a too-short lease is invisible: the system stays correct and quietly does less work than
  it appears to.
- **Two structural schema changes, not one.** The claim token *and* the primary key. A consumer who
  applies only `claim_token` gets a schema the code cannot query. The migration is published in the
  CHANGELOG with the unique index recreated **before** the old primary key is dropped, so the dedup
  gate is never absent, and with the instruction to drain the queue first — rows sitting in
  `Processing` when the key changes are the one case with no clean answer.
- **`AddAsync` returning a value does NOT force callers to read it, and this ADR previously claimed
  it did.** `await writer.AddAsync(message, ct);` compiles unchanged against
  `ValueTask<InboxWriteResult>` — discarding the value of an awaited expression used as a statement
  is legal C# and raises no diagnostic, even under `TreatWarningsAsErrors`. Verified by
  compilation, not assumed. Only a caller that assigned the returned `ValueTask`, or converted it
  with `.AsTask()`, breaks.
  The signal is therefore preserved by **`InboxMetrics`**, not by the type system:
  `microkit.inbox.messages.deduplicated` is recorded on the ingestion path regardless of what the
  caller does with the result. The return value makes the outcome *expressible* and stops the
  exception-as-nominal-path defect; it does not make reading it mandatory. A stronger guarantee
  would need `[MustUseReturnValue]`-style analysis, which is not in scope here — and a release note
  promising a protection that does not exist is worse than one that names the real mechanism.
- **`BatchSize` 20 → 100 and `MaxRetries` 10 → 5 are behavioural changes,** not merely new
  defaults. Both bind unchanged from existing configuration but halve the retry budget.
- **A redelivery is quiet in MicroKit's logs and noisy in EF's.** The module logs it at `Debug` and
  counts it; EF Core independently logs the rejected `INSERT` at `Error` before the store absorbs
  it. The setting that would silence that lives on the consumer's `DbContext`, so a library cannot
  set it. The harness asserts both halves rather than wishing the second away.
- **Retention is deliberately asymmetric with the outbox's and must stay so.** 30 days against the
  outbox's 7. On the outbox, deleting early loses history; on the inbox it loses the deduplication
  guarantee, because the table only deduplicates messages it still holds. The window must exceed
  the maximum plausible redelivery delay of every upstream transport.

### Alternatives considered

**`IMeterFactory` for `InboxMetrics`, as the design specified.** Rejected. It obliges every host —
including every bare `ServiceCollection` test host, and the end-to-end harness — to call
`services.AddMetrics()` or fail at resolution, and it pulls `Microsoft.Extensions.Diagnostics` into
Central Package Management on both sides. The only thing it buys is per-container meter isolation,
which nothing in this repository exercises. `InboxMetrics` owns its `Meter` instead; subscribing is
unaffected, since OpenTelemetry picks it up with `AddMeter(InboxMetrics.MeterName)` either way.
**If per-container isolation is ever needed, switching back is a one-line change in `InboxMetrics`
plus `AddMetrics()` in the consumer's composition root** — this was weighed, not overlooked.

**Deterministic `2^n` back-off, as the design specified.** Rejected. Its stated rationale was
symmetry with the outbox, and that had stopped being true: ADR-MSG-015's lot replaced the outbox's
deterministic curve with full jitter precisely because several processor instances make the rows
that fail together — which is what an outage produces — retry together, and keep doing so on every
subsequent attempt. The design's own justification therefore argued for jitter.

**A per-provider `SqlState` detector for the duplicate.** Rejected: it fails on a
`DEFERRABLE INITIALLY DEFERRED` unique constraint, where the inner exception is not a provider
exception at all, and it becomes dead code the day a PostgreSQL adapter uses
`ON CONFLICT DO NOTHING`. It also would not have helped under an ambient transaction: it classifies
the exception correctly and then returns into a transaction that is already dead.

**The `EntityFramework.Exceptions` package.** Rejected: it activates through
`UseExceptionProcessor` on the **consumer's** `DbContext`, which a library cannot configure, so a
consumer who forgets it silently reproduces the defect. It remains a good fit for an application
that owns its own `DbContext`; the constraint here is specific to being a library.

**`FOR UPDATE SKIP LOCKED` for the claim.** Rejected, as for the outbox: a PostgreSQL locking
clause in the provider-neutral EF Core package leaks a provider dependency and leaves the
production claim path untestable under SQLite. The token achieves the guarantee portably, and the
`READ COMMITTED` re-evaluation it relies on is demonstrated by the PostgreSQL suite rather than
assumed.

**OR-ed predicate pairs instead of a surrogate key.** Rejected: correct, but it builds a
variable-length expression tree that defeats plan caching — a poor trade on the hottest write path
in a library.

**Classifying `CreateScopeAsync` failures as configuration errors.** Not taken. The gap is real,
but per-tenant connection resolution can legitimately throw `InvalidOperationException`
transiently, and misclassifying it would stop the worker on a network blip. Without a typed
exception from `IExecutionScopeFactory` the two are indistinguishable, and guessing is worse than
not guessing. The right repair is startup validation — assert at registration that
`IInboxSettlementStore` and every registered handler type resolve — which is tracked, not silently
dropped.

---

## ADR-MSG-018: Integration Events Are a Marker Contract, and the In-Process Transport Is Withdrawn

**Status:** Accepted — **superseded in part by ADR-MSG-019 (three points, enumerated there)**
**Date:** 2026-08-24
**Supersedes (in part):** ADR-MSG-010 — the `IIntegrationEvent : IEvent` line stands; nothing else about the interface does
**Superseded (in part) by:** ADR-MSG-019 — §3's relocation of the fan-out into
`InProcessIntegrationDispatcher` (it was **withdrawn**, not relocated; the type does not exist), the
consequence about `AddInProcessTransport()` keeping its registrations, and the rejected alternative
that cited `InboxRedeliveryTests` as blocking evidence. **Everything else below stands** — read §3
against ADR-MSG-019's supersession list before relying on it.
**Related:** ADR-MSG-002, ADR-MSG-008, ADR-MSG-009, ADR-EXEC-001

### Context

`IIntegrationEvent` was a typed contract carrying its own identity and execution context:

```csharp
public interface IIntegrationEvent : IEvent
{
    MessageId MessageId { get; }
    string TenantId { get; }
    CorrelationId? CorrelationId { get; }
    CausationId? CausationId { get; }
    DateTimeOffset OccurredOnUtc { get; }
}
```

Every one of those fields also exists as a column on the message row, with nothing keeping the two
in agreement. An event built in a test, a tenant set before the ambient context resolved, and the
row is written with one tenant while the trace says another — a discrepancy nothing detects, on the
field that governs isolation.

**The review that settled this found the cause rather than the symptom.** The members were not a
design preference; they were forced by a seam. `IMessagePublisher.PublishAsync<T>(T evt, ct)`
receives an event and nothing else, so `InProcessMessagePublisher` — the only implementation ever
written — had to reconstruct `MessageId`, `TenantId`, `CorrelationId` and `CausationId` by reading
them off the event instance. It had no other source. The interface carried metadata because the
seam threw the metadata away.

`InProcessIntegrationDispatcher`, one call earlier, holds the `OutboxMessage` those four fields are
columns on.

### Decision

**1. `IIntegrationEvent` becomes a marker.** It loses every member and keeps the `IEvent` base. All
message metadata lives on `IntegrationEventMessage`, assigned by `IIntegrationEventPublisher` at
staging from the ambient `IExecutionContext`.

```csharp
[IntegrationEvent("saasbtp.safety.constat-recorded.v1")]
public sealed record ConstatRecorded(Guid ConstatId, Guid SiteId) : IIntegrationEvent;
```

**2. The contract name moves to `[IntegrationEvent]`.** The assembly-qualified CLR type name is not
usable as a wire identity: a namespace rename invalidates rows already in flight, and a consumer in
another service holds a different type in a different assembly, so the name could never match. This
is what makes extracting a module into its own service invisible to consumers.

**3. `IMessagePublisher` and `InProcessMessagePublisher` are deleted, and the fan-out moves into
`InProcessIntegrationDispatcher`.** Every field of the inbox row now comes from the outbox row.
This is the part that makes decision 1 possible rather than merely tolerable: removing the seam
removes the reason the members existed.

**4. `IIntegrationEventPublisher` requires an open transaction and says so loudly.** A row staged
with no transaction to commit it is not an error anyone sees — the change tracker is discarded and
the event silently never existed.

**5. Publication is registered explicitly per module,** with the contract name **and source** stored
per registration rather than in a shared options singleton, which the last module to register would
otherwise overwrite for all the others.

### Rationale

**Single source of truth, not aesthetics.** With the typed contract there is no mechanism keeping
`event.TenantId` and `row.TenantId` in agreement. The marker removes the question: there is one
tenant, read from the execution context at staging.

**The identity was already contradictory — and the contradiction was load-bearing.** The interface
declared a `MessageId`, but the value the inbox deduplicates on is the *outbox row's* id. Reading it
off the deserialized event survived redelivery only by accident: the same payload happens to
deserialize to the same value, but nothing guaranteed it — not the contract, not the serializer, and
not an event type free to compute its identity in a property initializer. Sourcing it from the row
makes the guarantee structural. That latent bug is closed by decision 3, not merely avoided.

**The `MicroKit.Domain` coupling is solved by the transport boundary, not by the hierarchy.** A
transport carries an envelope — contract name, source, serialized payload, metadata — and none of
those fields requires knowing the event type. Serialization happens at publication, inside this
module, so nothing downstream ever names `IIntegrationEvent`. The coupling exists and stops at the
edge of one assembly, which is why `IEvent` stays.

**Two timestamps, because they are two facts.** `CreatedAtUtc` is when the row was staged and is
always set; `OccurredOnUtc` is when the fact happened and is null when the caller did not say. A
claim orders on the first — ordering on the second would let a backdated event jump the whole queue
and make queue order depend on caller-supplied data.

**A separate table.** The outbox carries domain event notifications and is drained by a MediatR
fan-out. Behind a discriminator, one misregistration would feed integration events into that
fan-out with a `WHERE` clause as the only thing preventing it. Separate tables make the mistake
inexpressible rather than detectable — and that exact confusion has already cost this codebase once.

### Consequences

**The in-process transport is withdrawn.** `IMessagePublisher` had one implementation and one
consumer; both are gone. `AddInProcessTransport()` keeps its `IMessageSerializer` and
`IOutboxDispatcher` registrations — removing the latter would break `AddMediatRDomainEvents()`,
which throws without a dispatcher to decorate. A real transport seam arrives with the transport
libraries; nothing here is generalised in anticipation of it.

**Breaking on a published package, deliberately now.** Both `MicroKit.Messaging.Abstractions` and
`MicroKit.Messaging` are `1.0.0-preview.*` with no external consumers. After `1.0.0` neither change
is available at any reasonable cost.

**An integration event instance no longer knows its own identity.** Code that needs it takes the
`MessageId` returned by `PublishAsync`. That is a real loss for a handler wanting to log the id
before staging — and it is the point: before staging there is no message, therefore no identity.

**`MessageEnvelope<T>` still declares `MessageId`, `TenantId` and `OccurredOnUtc` as its own
parameters.** It is dead public API — nothing in v1 constructs, consumes or transmits one — and is
reserved for the transport work. Left alone deliberately; it is not evidence the metadata still
belongs on the contract.

**Composition is split three ways**, because `MicroKit.Messaging` has no EF Core dependency and must
not acquire one:

```csharp
services.AddIntegrationEventContracts("/saasbtp/safety", e => e.Publishes<ConstatRecorded>());
services.AddMicroKitMessaging()
        .AddIntegrationEventPublishing()             // registry + publisher + startup validation
        .AddEfCoreIntegrationEvents<AppDbContext>(); // the staging writer
```

**Startup validation is not optional.** The registry is a factory-built singleton, so it would be
constructed on first resolve — which on this path is the first publication, inside a handler, inside
a transaction. A duplicated contract name would roll that transaction back and be classified as a
transient failure, retrying forever against something no retry can fix.
`IntegrationEventRegistryValidator` resolves it at boot instead.

**`ClaimToken` is mapped as an EF concurrency token**, unlike `OutboxMessage.ClaimToken` and like
`InboxMessage.ClaimToken`. The relay that consumes it is a later lot; the column belongs to the row
now, and leaving the mapping to that lot means the day someone forgets, no test that does not
exercise concurrency will notice. Pinned by a model-metadata assertion for exactly that reason.

### Enforcement

- `MicroKit.Domain` must not reference the publishing assembly.
- In the consuming application, because these are application rules a library cannot impose:
  no aggregate references `IIntegrationEvent`; no `ICommandHandler` references
  `IIntegrationEventPublisher`; every `IIntegrationEvent` in a module is registered; and the set of
  contract names matches a snapshot, so an accidental rename fails the build.

### Alternatives considered

**Keep the typed contract and drop the execution context as a source.** Would make the event the
single source of truth instead. Rejected: it forces every notification handler to populate five
fields correctly on every event, and puts tenant plumbing into the code that translates a domain
fact — precisely the coupling the execution scope exists to remove.

**Keep both and reconcile at staging.** Rejected: it makes the discrepancy silent rather than
impossible, and "reconcile" would mean choosing a winner on the tenant field.

**Make the interface a marker but keep `IMessagePublisher`.** Rejected on discovery that it is not
possible: the publisher has no other metadata source, so the in-process path would have had to be
given an envelope — the transport work this lot explicitly defers. Keeping the seam and keeping the
members was the only consistent alternative, and it preserves the defect.

**Have `InProcessIntegrationDispatcher` throw "no transport registered" instead of fanning out.**
Rejected on evidence: the claim that no producer of integration events exists is false.
`MicroKit.Messaging.MediatR.IntegrationTests.InboxRedeliveryTests` drives that exact path and
asserts the fan-out, and it must pass unchanged.

**Move `IIntegrationEvent` into `MicroKit.Domain` next to `IDomainEvent`.** Rejected: if both
interfaces live in the assembly the domain references by construction, "no aggregate references
`IIntegrationEvent`" stops being a dependency constraint and becomes a type inspection — verifiable,
but weaker and easier to work around.

**Upcasters instead of a version suffix in the contract name.** Rejected as premature: schema
evolution machinery carries a permanent maintenance cost before a single breaking change has
occurred. Versioning stays additive-only. Revisit under the rule of three.

---

## ADR-MSG-019: The Reentrant Outbox — One Table, Routed by Kind, Decorated Not Replaced

**Status:** Accepted
**Date:** 2026-08-25
**Supersedes (in part):** ADR-MSG-018 — three specific points, enumerated below; the rest stands
**Related:** ADR-MSG-002, ADR-MSG-009, ADR-MSG-016, ADR-MSG-017, ADR-MEDIATR-014/-015

### Context

ADR-MSG-018 made `IIntegrationEvent` a marker and moved every field of message metadata onto the
row. It could not finish the job, and said so: the in-process fan-out stayed, because no transport
existed to replace it, and `MediatROutboxDispatcher` still routed by testing the deserialized
payload for `is INotification`.

Both are now wrong for the same underlying reason. The outbox is **reentrant** — one table carrying
two natures of row — and the nature of a row is a fact about the row, not about a CLR type someone
can recover from it. A type test is invisible to SQL, so an operator cannot ask what is stuck in the
queue. It forces the payload-agnostic core to know about the notification abstraction it
deliberately does not reference. And it only works in process at all by accident: a consumer in
another service holds neither the producer's assembly nor its types.

### Decision

**1. One table, two natures, routed by `MessageKind`.** A domain event is staged as a
`Notification` and fanned out in process; a handler in that fan-out may publish an integration
event, staged as a `Contract` and handed to a transport on a second pass through the same queue.
The two share one set of reliability machinery — claim, lease, back-off, dead-letter — and two
tables would mean two processors and two copies of it, in two packages, with nothing keeping them
from diverging.

**2. `MicroKit.Messaging.MediatR` decorates; it does not replace.**

| Composition | Behaviour |
|---|---|
| Core alone | every row goes to the transport |
| Core + `.MediatR` | `Kind = Notification` → `IPublisher.Publish`; everything else → the inner dispatcher |

Core never sees `INotification`. A user who does not install `.MediatR` gets a fully working
messaging path; installing it changes the behaviour of notifications only.

**3. The decoration uses a keyed inner seam, so no registration order can bypass it.** Two slots
with two owners: `AddTransportDispatcher()` writes the standard dispatcher into a keyed slot
(`OutboxDispatcherKeys.Standard`) that only Core ever touches, plus an unkeyed `TryAdd` forwarder;
the glue removes unkeyed `IOutboxDispatcher` descriptors and takes that slot outright, resolving its
inner through the key.

| Order | Result |
|---|---|
| transport → glue | glue removes the forwarder and wraps the keyed dispatcher |
| glue → transport | keyed `TryAdd` lands; the unkeyed one correctly abstains |
| glue alone | keyed lookup yields null — a legal composition, see decision 5 |
| glue twice | remove-then-add nets one descriptor |

**4. `InProcessIntegrationDispatcher` and `AddInProcessTransport()` are deleted.** The in-process
fan-out wrote inbox rows on the *producing* side, which is the confusion the contract-name
indirection exists to remove. `AddInProcessTransport()` would otherwise have survived as a public
method registering nothing but a JSON serializer.

**5. The inner dispatcher is optional, and a notification-only host is a first-class composition.**
It calls `AddMediatRDomainEvents()` and no transport method at all. A `Contract` row in such a host
raises `OutboxConfigurationException`: batch released, no retry consumed, rows stay `Pending`, the
worker stops.

**6. Per-message settlement** (implemented separately): `ApplyOutcomesAsync` moves inside the loop,
narrowing the replay window from one batch to one message.

**7. The replay guard is a natural key, not a settlement store.** `(OriginMessageId, ContractName)`
unique. A replay re-runs the notification handler, which publishes the same contract from the same
origin row, which collides; the publisher absorbs the collision as "already published".

The column's naming path is `CausedByMessageId` → `SourceMessageId` → **`OriginMessageId`**, and the
middle name reached `dev` by accident rather than by decision. The design session called it
`CausedByMessageId`; that was rejected because the value is structural and cannot degrade to null,
unlike `CorrelationId` and `CausationId`. It shipped as `SourceMessageId`, which the api-reviewer
then rejected in turn: `Source` already means *the emitting module* in this package, and both
notions land on `OutboxMessage` once the dedicated integration-event table is retired. The commit
applying that review was pushed to its source branch two minutes after the PR had been squash-merged
and closed, so it never landed, and three further PRs built on the un-renamed column before anyone
noticed. Recovered under *Fixed* in the module CHANGELOG. `OriginMessageId` is the settled name.

**8. `IOutboxSettlementStore` is abandoned.** `IInboxSettlementStore` works because one inbox row =
one handler = one transaction, so "the" transaction to stage the mark into is unambiguous. The
outbox fans out: one row, N handlers, N transactions. If the mark commits with handler A and handler
B then fails, the row is `Published` and B is never replayed. There is no single transaction to
stage into, and per-message settlement plus the natural key reach the goal without having to answer
an unanswerable question.

**9. Handlers registered with no producer fail at boot.** `InboxIngestionValidator` throws when
`MessageHandlerRegistry` holds an entry — see Consequences.

### Two behavioural decisions, recorded so neither reads later as an oversight

**`AddTransportDispatcher()` with no `IMessageTransport` now stops notifications too.**

| | Before | After |
|---|---|---|
| `AddTransportDispatcher()`, no transport, notification row | dispatches — the inner was never reached | `OutboxConfigurationException`, batch released |
| `AddTransportDispatcher()`, no transport, contract row | `OutboxConfigurationException` | unchanged |
| no `AddTransportDispatcher()`, notification row | not composable — the glue threw at registration | dispatches |

The decorator activates its keyed inner when constructed, so a missing transport fails inside the
resolution `OutboxProcessor` wraps, before any row is examined. Calling that method declares an
intent to send contracts; a host that only fans out notifications now has a composition that says
exactly that, and did not before. The old behaviour bought working notifications at the price of a
host sitting half-composed indefinitely — contracts piling up `Pending` while notifications drained
and everything looked healthy. **Lazy resolution inside the `Contract` branch is rejected:** it needs
a service-locator dependency in the decorator plus a second copy of the
`InvalidOperationException` → `OutboxConfigurationException` conversion that
`OutboxProcessor.ResolveDispatcher` already owns, and a second copy is how a classification drifts.

**An unknown `MessageKind` reaching the decorator with a null inner is released, not dead-lettered.**
`TransportOutboxDispatcher` dead-letters one, correctly: it is a fully composed build, so a kind it
cannot interpret is permanent for that deployment. With no inner registered this build is by its own
admission incomplete — the missing registration may be the very package that understands the kind —
so the reversible verdict is the honest one. A row must not be destroyed on the strength of a host
that was never finished assembling. Where an inner *is* registered the row is delegated and the
inner's verdict stands unchanged.

### The registry's publishing-only position is reversed

Recorded here because it has been living in `IntegrationEventRegistry`'s XML docs since the contract
registry landed, and a decision of this weight does not belong in a class comment.

`IntegrationEventRegistry` is **bidirectional**. `ResolveContract(Type)` gives the wire name a type
publishes under; `ResolveLocalType(name)` gives the local CLR type a wire name deserializes into.
The reverse direction is the precondition for any transport: a consumer holds a payload and a name,
not the producer's assembly, so `Type.GetType(assemblyQualifiedName)` cannot resolve across a
process — it works in process only by accident. `Publishes<T>()` binds both directions, so a modular
monolith routes its own contracts without a second declaration; `Consumes<T>()` exists for a
contract a module does not publish itself, and declaring both is a no-op rather than a conflict —
a module must not have to know whether its dependency happens to be in-process. One local type per
contract name per process; a second claimant is a boot failure, because a relay deserializes once
before any fan-out and an ambiguous name would be resolved by picking arbitrarily, silently
producing the wrong type from structurally compatible JSON.

### What this supersedes in ADR-MSG-018 — three points, and no more

1. **§3's "the fan-out moves into `InProcessIntegrationDispatcher`".** The fan-out is withdrawn, not
   relocated. The reasoning that justified the move — the row, not the event, is the authoritative
   source of message metadata — remains correct and is now carried by `TransportOutboxDispatcher`,
   which builds every envelope field from the row.
2. **The consequence "`AddInProcessTransport()` keeps its `IMessageSerializer` and
   `IOutboxDispatcher` registrations — removing the latter would break `AddMediatRDomainEvents()`,
   which throws without a dispatcher to decorate."** No longer true in either half: the decorator
   tolerates a null inner, and the serializer default now lives on `AddMicroKitMessaging()`, which is
   where it belongs — Core registers `InboxProcessor` and `OutboxMessageFactory`, and both require
   one, so an optional builder method was never the right owner.
3. **The rejected alternative that cited `InboxRedeliveryTests` as blocking evidence** — below.

Everything else in ADR-MSG-018 stands: `IIntegrationEvent` as a bare marker; `[IntegrationEvent]` as
the wire identity; metadata assigned at staging from `IExecutionContext`; ~~`IntegrationEventMessage`
on its own table~~ (**retired by step 5 — see the implementation note at the end of this ADR**); `IIntegrationEventPublisher` requiring an open transaction the caller owns;
per-module registration carrying `source`; `IMessagePublisher` and `InProcessMessagePublisher`
staying deleted; the two timestamps; `ClaimToken` as an EF concurrency token.

### The retirement of `InboxRedeliveryTests`

ADR-MSG-018 rejected "have `InProcessIntegrationDispatcher` throw *no transport registered* instead
of fanning out" with: *"Rejected on evidence: `InboxRedeliveryTests` drives that exact path and
asserts the fan-out, and it must pass unchanged."* **That rejection was correct when it was
written.** This ADR deletes the test, so it must record what the test proved — otherwise the option
it closed becomes an option nobody examined.

`Redelivery_AfterInboxRowsWritten_IsDeduplicatedAndTheOutboxRowIsPublished` drove one command to one
outbox row, then drained twice with a forced redelivery in between, and asserted five properties:

1. one outbox row fans out to exactly **one inbox row per registered `ConsumerType`** — two here;
2. the second drain's duplicate insert is absorbed by the inbox unique index and reported as
   `InboxWriteResult.AlreadyPresent`, never thrown;
3. the redelivered outbox row therefore reaches `Published` with **`RetryCount == 0`** — the
   regression it existed to prevent was a correctly-delivered message being retried to death and
   dead-lettered because a redelivery escaped as an exception;
4. a duplicate for one consumer does not cost the consumers after it their rows;
5. nothing logs at `Warning` or above — a redelivery is nominal, not a fault.

Note what it did *not* assert: the handlers never ran. The inbox rows were the delivery evidence,
because the drain loop was never driven.

**What changed is that the fan-out is no longer the only way to reach those properties.**
`TransportOutboxDispatcher` builds a `MessageEnvelope` carrying `MessageId` from the row — the same
stable identity the fan-out copied into `InboxMessage.MessageId`, for the same stated reason.
Property 1 becomes the receiver's subscription fan-out, and properties 2–5 become the receiver's
inbox, both over the wire. On the producing side, the `(OriginMessageId, ContractName)` unique index
already covers the replay this test was probing.

**What is genuinely uncovered until the receiving seam lands:** the *end-to-end* assertion that a
redelivered dispatch produces no duplicate and no dead-letter. The mechanism itself is still pinned
by `IntegrationTests/Stores/InboxIngestionTests.cs`
(`Redelivery_is_reported_as_already_present_and_does_not_throw`,
`A_duplicate_for_one_consumer_does_not_block_the_others`,
`The_unique_index_is_what_rejects_the_duplicate`); only the composition through a dispatcher is not.
**That step owes this test back.**

### Consequences

**The inbox is unfed, not broken, and the distinction is the whole risk of this lot.** Nothing in
`MicroKit.Messaging` writes an `InboxMessage` any more — `InProcessIntegrationDispatcher` held the
only call to `IInboxWriter.AddAsync`. `InboxProcessor`, `InboxWorker`, `SharedDbInboxCoordinator`,
both retention workers, all five store interfaces and `MessageHandlerRegistry.TryGetInvoker` are
unchanged, still correct, still registered, and still proven end-to-end by
`InboxSettlementGuaranteeTests`, which seeds its own rows. They have no producer. Pinned by
`Core_DoesNotDependOnIInboxWriter`, which is **expected to fail when the receiving seam arrives** —
deliberately, so that step revisits this ADR instead of quietly reinstating an in-process producer.

**A registered handler is therefore a boot failure.** `InboxIngestionValidator` throws
`InboxConfigurationException` naming the consumers and the reason. Without it the shortfall is
undetectable: no row is written, the processor claims nothing, logs nothing above `Debug`, and
reports a healthy empty queue indistinguishable from an idle one. Like
`IntegrationEventRegistryValidator` it is a hosted service, so it reaches a real host only — a test
container built with `BuildServiceProvider()` is unaffected. **Delete it with the receiving seam.**

**`MessageHandlerRegistry.GetHandlers` loses its only caller and is kept.** It is the seam the
receiving side needs — a wire name resolves to a local type, and that type resolves to its consumers
here. Covered directly by `MessageHandlerRegistryTests` so it is an uncalled seam rather than
untested code.

**`AddInProcessTransport()` is removed outright, a breaking change on a preview package.** Migration:
call `AddTransportDispatcher()`, or nothing at all for a notification-only host.

**No `[Obsolete]` bridge, and that is a standing policy rather than a fresh judgement.** ADR-MSG-016
§4 settled it when `AddMediatRTransport()` was renamed outright: *"Both packages are
`1.0.0-preview.*` with zero external consumers and one in-repo call site — this is the cheapest the
rename will ever be,"* and it explicitly rejected keeping the old name behind an alias because that
*"commits to carrying it past 1.0.0."* Every term of that argument holds here unchanged. The
consequence is that a consumer meets `CS1061` with no text attached, so the migration line above and
the CHANGELOG entry are the only places it is written down — which is why both say the same thing in
the same words. Revisit this policy at 1.0.0, not per-removal: an unexplained break and a consistent
one read very differently to whoever hits it.

**The decorator no longer deserializes a contract row**, which closes the double-deserialization
item the design session left open. It is also the discipline most easily broken by accident:
deserializing in front of `TransportOutboxDispatcher` would reinstate the producer-type-graph
coupling that class was built to avoid while leaving it looking correct. Pinned by
`DispatchAsync_WhenKindIsContract_NeverTouchesTheSerializer`, which uses a recording fake rather than
`DidNotReceive()` — a negative assertion against a mock stays green if the subject starts calling a
different method.

**`E2EHarness` is now the standing proof of the notification-only composition.** It registers no
transport at all, and its four suites pass unchanged.

### Alternatives considered

**Keep descriptor surgery (`LastOrDefault` + `Remove` + `CreateInner` + a marker descriptor) and just
relax the "nothing to decorate" precondition.** Smaller diff, no new public constant. Rejected: it
leaves glue-then-transport wrong. Core's `TryAdd` would find the decorator holding the only slot and
abstain entirely, so the transport dispatcher would never be registered and every contract row would
fail with a message telling the operator to call a method they had already called.

**A route collection — Core owns one `IOutboxDispatcher` router resolving `IEnumerable<IOutboxRoute>`,
each package contributing a route per `MessageKind` via `TryAddEnumerable`.** Order-independent by
construction, and the same shape as the `IDomainEventsSink` fix in ADR-MSG-016. Rejected on cost, not
on merit: it reshapes `TransportOutboxDispatcher` one step after it shipped, adds a public
Abstractions type, and carries the identical wrinkle that resolving the collection activates every
route. Worth revisiting if a third nature of row ever appears.

**Delete `MessageHandlerRegistry.GetHandlers` along with its caller.** Rejected: the receiving seam
rebuilds it two steps later, and a direct test costs less than the round trip.

**Leave the unfed inbox silent and document it.** Rejected: it is precisely the silent gap this
module treats as blocking, and documentation is not a signal a running host can emit.

---

### Implementation note — step 5: the publisher writes Contract rows (2026-08-27)

The last decision this ADR left open is closed. `IIntegrationEventPublisher` writes a
`MessageKind.Contract` row into the outbox inside the caller's transaction;
`IntegrationEventMessage`, `IntegrationEventStatus` and `IntegrationEventMessageConfiguration` are
deleted, and `NoAssemblyStillCarriesTheDedicatedIntegrationEventTable` blocks their return.

**Five things were decided while implementing it that the ADR did not anticipate.**

1. **The staging writer had to start flushing, and the "never save" rule was an overstatement.**
   `EfInboxStore.AddAsync` can absorb a unique violation because it owns its `SaveChangesAsync`;
   `EfIntegrationEventWriter` did not, so the `INSERT` landed at the caller's `CommitAsync` — one
   frame after `PublishAsync` returned, where nothing can absorb it. There, `EfUnitOfWork` wraps it
   as `PersistenceException`, `OutboxProcessor` classifies it transient, and a handler that catches
   `PersistenceException` defensively turns a rolled-back dispatch into a `Published` mark. The
   invariant the rule protects — *never commit* — survives the flush intact: the write is inside the
   caller's transaction and a rollback still erases it. What the flush does change is that the
   caller's other pending changes are written at that point too. **Verified rather than argued**:
   `AbsorbedDuplicate_LeavesTheCallersOwnWritesIntact` proves EF leaves them `Added` across the
   savepoint rollback, in both `AutoSavepointsEnabled` configurations, because only the pair
   distinguishes belt-and-braces from load-bearing.

2. **`OriginMessageId` travels on a scoped `OriginMessageHolder` written by `OutboxProcessor`**, not
   through `IExecutionContext.Properties`. A custom `IExecutionScopeFactory` that rebuilt the context
   without copying the bag would leave the origin null — which does not throw, it silently disables
   deduplication, because nulls are distinct in the index. The processor stamps the scope it received
   so no factory can drop it.

3. **Retention is a state guard, not a duration.** A contract row is never purged while the row its
   `OriginMessageId` names still exists and is not `Published`. No default could work: automatic
   replay is bounded by `MaxRetries × MaxRetryBackoff`, both configurable, and an operator requeue of
   a dead-lettered origin is unbounded. The duplicate this prevents is the one nothing downstream can
   recognise — a republished contract carries a *fresh* `MessageId`, so a consumer's inbox sees a
   message it has never seen.

4. **Two behavioural changes fall out of the row's shape.** `OutboxMessage.CorrelationId` is
   non-nullable, so an unparseable correlation now becomes a fresh one rather than null; and
   `OccurredOnUtc` is non-nullable, so an unstated occurrence time is resolved to the staging time at
   staging rather than at the transport. Both are visible only in the logs.

5. **The causation chain was never built, and closing it is a wire-visible change.** Both processors
   copied the dispatched row's *own* `CausationId` into the scope they created, which names the
   grandparent rather than the row being processed. Nothing assigns a causation at the root either,
   so the copy propagated null forever: `CausationId` was null on every row of every path, while the
   column, the value object and `MessageEnvelope.CausationId` all documented a link that did not
   exist. Both processors now **derive** it — `OutboxProcessor` from `message.Id`, `InboxProcessor`
   from `message.MessageId` and never `RowId`, which is a local surrogate that resolves in no other
   process. Correlation is still copied unchanged; the two are propagated by adjacent lines and a
   test asserting only one passes while the other is confused for it, which is how this survived.
   **Not merely a column filling in.** The value reaches the wire through
   `MessageEnvelope.CausationId`, so a consumer that inferred "always null" starts seeing values, and
   `IExecutionContext.CausationId` populates inside message scopes. Nothing branches on it — no
   index, no query, no routing test — so no behaviour regresses. Do not merge this with
   `OriginMessageId`, although the outbox derives both from `message.Id`: they coincide in value and
   differ in obligation, the origin being half of a unique key that may never degrade, the causation
   diagnostic and degrading to null via `OutboxMessageFactory.ResolveCausation`. The full write-up
   is in `CHANGELOG.md`.

**The test ADR-MSG-019 recorded as owed is repaid.** `ReentrantOutboxTests.Redelivery_ProducesNoDuplicateContract`
drives a command through the fan-out to a publication and a transport send, then forces the
redelivery an expired lease produces, and asserts the properties `InboxRedeliveryTests` carried: one
contract row, `RetryCount == 0`, no dead-letter, and nothing from MicroKit's own log categories at
`Warning` or above. (EF Core logs the rejected `INSERT` at `Error` from its own category on every
absorbed replay; that setting lives on the consumer's `DbContext`, so a library cannot silence it.)
