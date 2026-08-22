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
