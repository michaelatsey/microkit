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

**Status:** Superseded by ADR-MSG-020 — the publisher is deleted; cascade events are dispatched by a receive middleware, after each handler and before the engine's save, and are delivered  
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
**Amended by:** ADR-MSG-020 — the sink stands (it sends through `ILocalMessageSender` now); `AddMediatRDomainEvents()` is replaced by `UseMediatRDomainEvents()`, and the decorator and the cascade publisher it registered are deleted.

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
**Amended by:** ADR-MSG-020 — its four-transaction model (ingestion, claim, the handler's transaction with the staged mark, the deferred settlement) is replaced by one transaction per handler on one receive pipeline; `IInboxWriter`, `IInboxSettlementStore` and `InboxWriteResult` are deleted for `IInboxGuardStore`. The atomic claim, the token fencing, the `RowId` key and the retention rule stand.

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
unaffected, since OpenTelemetry picks it up with `AddMeter(MessagingDiagnostics.InboxMeterName)`
either way. (#185 made `InboxMetrics` internal and moved the name to `MessagingDiagnostics`; the
value is unchanged.)
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

**Status:** Accepted — **superseded in part by ADR-MSG-019 (three points, enumerated there) and by ADR-MSG-020 (§4)**
**Superseded (in part) by:** ADR-MSG-020 — §4, "`IIntegrationEventPublisher` requires an open transaction": the publisher is deleted, and a publication through `IMessageBus` is *enlisted* in the store's `DbContext`, with no transaction required; a row its scope never saves is reported (event 4000).
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
**Superseded (in part) by:** ADR-MSG-020 — §1, routing by `MessageKind` (the column is replaced by `Endpoint`, and a dispatcher is resolved keyed by it); the decorator and the keyed `OutboxDispatcherKeys.Standard` slot (deleted); §7, the replay natural key `(OriginMessageId, ContractName)`, its unique index and its savepoint path (removed: a redelivered handler's publications commit or skip with its inbox row). One table, the reentrant hop and the opaque transport payload stand.

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

> ⚠ **Superseded in part** by the implementation note *outbox resolution classification
> (2026-09-13)*, below. The two `OutboxConfigurationException` cells for `AddTransportDispatcher()`
> with no transport now read: batch released, no retry consumed, **not rethrown** —
> `OutboxBatchAbortReason.DispatcherActivationFailed`, the worker backs off. The argument against lazy
> resolution stands, on a new ground. This note is not rewritten.

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

---

### Implementation note — step 7: the receiving seam (2026-09-05)

The inbox has a producer. `IEnvelopeReceiver` (Abstractions) is implemented by `EnvelopeReceiver`
(`internal sealed`, Core) and called by a transport provider's consume loop; it resolves
`MessageEnvelope.ContractName` to this process's own type, and writes one `InboxMessage` per
registered consumer. `InboxIngestionValidator`, `InboxIngestionValidatorTests` and the
`AddMessageHandler<,>()` boot failure are **deleted** — the gap they stood in for is closed.

The end-to-end assertion this ADR recorded as owed is repaid by
`ReceivingSeamTests.AnEnvelopeCrossingTheSeam_CausesTheHandlersWorkThroughTheInboxRow`, and the
redelivery properties by `Redelivery_ThroughTheSeam_CostsNoConsumerItsRow`.

**Six things were decided while implementing that the ADR did not anticipate.**

1. **`Core_DoesNotDependOnIInboxWriter` was replaced, not deleted — and its real subject was
   narrower than its name.** The ADR predicted this failure deliberately, so that the step revisited
   the decision instead of quietly reinstating a producer-side writer. It failed exactly as
   predicted. But "Core must not reference `IInboxWriter`" was never the property worth pinning:
   what two rewrites spent their effort removing was a fan-out writing a *consumer's* row inside the
   *producer*, bypassing the wire and the contract name that make the consumer's own type
   resolvable. A row written on the receiving side, from an envelope that crossed a transport, is
   the opposite of that defect. The replacement, `NoOutboxDispatcherWritesInboxRows`, asserts that
   no `IOutboxDispatcher` in Core **or in the `.MediatR` glue** depends on `IInboxWriter` — the glue
   included because `MediatROutboxDispatcher` is the other place a producer-side writer could be
   added unnoticed. It carries **two** controls rather than the original's one: the assertion is now
   a conjunction, so it can go vacuous on the selector as well as on the dependency query.

2. **The S4 verdict: `InboxPayloadException` is reused, and no receive-side exception type was
   introduced.** An unbound contract name is permanent — the binding cannot appear without a
   redeploy — and permanent is exactly what that type already means. It is in practice the module's
   general permanent-inbox-fault type rather than a payload-specific one: it already covers "unknown
   consumer type", which is not a payload fault either. Reusing it leaves a provider **one** type to
   catch for "dead-letter this", and it already carries the *never throw for a timeout, a refused
   connection, an HTTP 503* guard rail a provider needs. Its `<summary>` was widened from "an inbox
   row" to cover an inbound envelope, and a bullet names the ingestion case. The one thing genuinely
   different — that there is no row to dead-letter, so the verdict must reach the broker — is stated
   on `ReceiveAsync`'s `<exception>` doc, where a provider reads it.

3. **S2's "the receiver owns and commits its own transaction" is met without one, and it has to
   be.** `MicroKit.Messaging` (Core) references only Abstractions, `MicroKit.Execution.Abstractions`
   and three `Microsoft.Extensions.*.Abstractions` packages: it has neither EF Core nor
   `ITransactionalContext`, and **structurally cannot open a transaction**. `IInboxWriter.AddAsync`
   already covers the case — with no ambient transaction it commits — so `ReceiveAsync` returns only
   after every row has committed, which is precisely the durability the acknowledgement rule
   demands. What is *not* obtained is atomicity across the fan-out, and it should not be: the rows
   advance independently by design (ADR-MSG-017), and a failure after the first row is absorbed on
   redelivery as `AlreadyPresent`. Recorded because the ADR's wording implies a transaction the
   package cannot open.

4. **The seam creates the execution scope itself, and uses `IExecutionScopeFactory` rather than
   `IServiceScopeFactory`.** Not a style choice: a tenant-aware factory resolves a per-tenant
   connection, and a row for tenant X must land in tenant X's database — so the envelope's tenant
   has to be in the context *before* `IInboxWriter` is resolved. Creating the scope internally also
   makes the "never share a scope across messages" rule structural instead of documented, which is
   what lets the receiver be a singleton a consume loop can hold. One scope per **envelope**, not
   per consumer: every row of a fan-out carries the same payload and tenant.

5. **Causation is copied onto the row and derived into the scope, and the two are one hop apart.**
   `InboxMessage.CausationId` takes the envelope's — the producer assigned it and this row does not
   reassign it. The receive scope's `IExecutionContext.CausationId` is derived from
   `envelope.MessageId`, matching both processors. The end-to-end test seeds them differently so
   neither can pass as the other.

6. **`MessageHandlerRegistry.RegisteredConsumerTypes` outlived its only caller and is kept.** It
   existed so `InboxIngestionValidator` could name the registered consumers in its boot failure. It
   is now the mirror of `GetHandlers` — which this ADR kept through one step with no caller, on the
   argument the receiving side would need it, and which `EnvelopeReceiver` now uses. Deleting it
   would be an unrequested public break for no gain; it stays covered directly by
   `MessageHandlerRegistryTests`. Worth revisiting at 1.0.0, not before.

**One correction to the passing order, recorded because the same class of drift produced the step-5
corrections.** The mutation check was specified as "nulling the derivation turns this test red and
nothing else". It turns **three** red, not one: the new end-to-end test, and the two
`InboxProcessorTests` cases that have covered that line at unit level since step 5
(`ProcessBatch_NamesTheDeliveredMessageAsTheCauseOfTheHandlersWork` and
`ProcessBatch_PropagatesCorrelationUnchangedWhileDerivingCausation`, the latter added by the step-5
closure lot). All three name that line by their own titles, so this is coverage, not coupling. The
specification was written from the design session rather than from the tree.

**Verified by mutation, four times, not by reading.** Nulling the causation derivation → three red,
listed above. Turning the `continue` on `AlreadyPresent` into an early exit → three red, including
the end-to-end redelivery test. Stamping `EventType` from the contract name instead of the resolved
type → three red. Stamping `ReceivedAtUtc` from `OccurredOnUtc` → one red, and note that the naive
form of that mutation **does not compile at all**: `timeProvider` becomes unread and `CS9113`
is an error under Release, which is a second, free guard on that column.

### Implementation note — step 7 review closure: the two clocks, and what a catch was hiding (2026-09-06)

The distributed-context review of the receiving seam. Seven findings; four applied as code, three as
documentation, one deliberately not applied. The four focus areas it was asked to verify — the
per-envelope scope, the copied/derived context, the fan-out, and whether the durability guarantee
survives having no transaction — all held as designed, so what follows is what the design did **not**
already cover.

**1. `InboxConfigurationException` meant more than "not registered", and the extra meaning was the
dangerous one.** `ResolveWriter` documented itself as a structural test — "the try wraps the
container call and nothing else" — and it was not one. `IInboxWriter` resolves through a factory
that activates `EfInboxStore<TContext>` and, with it, the consumer's `DbContext`, so
`catch (InvalidOperationException)` around `GetRequiredService` spanned that entire graph while
reading as a registration lookup.

The case that matters is the one step 7 §4 chose `IExecutionScopeFactory` *for*: a tenant-aware
factory resolves a per-tenant connection during that activation, and an envelope naming an unknown
or de-provisioned tenant throws exactly `InvalidOperationException` from it. Caught, it was reported
as a missing registration that was in fact present, told "retrying will not help", and — since the
exception instructs a provider to stop consuming — **one tenant's bad row would halt ingestion for
every tenant**. The scope choice and the resolution guard were written in the same step and did not
account for each other.

The fix is the structural test the code claimed to be making: `GetService` plus a null check.
`GetService` returns null only when nothing is registered and throws when something is registered
and cannot be activated, so the two verdicts are separated by the container rather than by
inspecting an exception. An activation failure now propagates unchanged and a provider nacks one
message. `ReceiveAsync_WhenTheWriterCannotBeActivated_PropagatesRatherThanClassifying` pins it, and
uses `InvalidOperationException` deliberately — the one type a catch-based implementation would
swallow.

**`OutboxProcessor.ResolveDispatcher` has the same shape and is left alone, owed.** Its
`StampOrigin` sibling is harmless (`OriginMessageHolder` is an internal marker with no
dependencies), but `ResolveDispatcher` activates a real graph. Out of scope for a review of this
changeset; it should be corrected the same way.

**2. The business clock was dropped at the seam, and that is the step-5 `CausationId` defect
again.** `EnvelopeReceiver` read seven of the envelope's eight members and never `OccurredOnUtc`;
`InboxMessage` had no column for it. Since ADR-MSG-018 made `IIntegrationEvent` a bare marker a
payload carries no timestamp of its own, so the business time existed **nowhere** on the receiving
side and a handler reading `ReceivedAtUtc` was reading the relay's clock for a business fact — while
`MessageEnvelope.OccurredOnUtc`'s own documentation described it as consumer-facing. Structurally
identical to what step 5 fixed on `CausationId`: an envelope member documented for the consumer that
never reached the consumer.

**Decided: add the column, now.** No broker provider ships, so it costs nothing today; later it is a
schema migration on every deployed consumer. `InboxMessage.OccurredOnUtc` is non-nullable and
written verbatim from the envelope.

**`ReceivedAtUtc` stays the claim's ordering key and `OccurredOnUtc` enters no index, no claim and
no ordering.** It is caller-supplied at the far end of a wire — an optional parameter of a public
publish method — so ordering on it lets a backdated event jump the queue and keep jumping it, which
is the defect the outbox claim left behind when it moved to `CreatedAtUtc`. Two tests, because the
schema and the query fail differently: `ClaimBatchAsync_OrdersOnReceivedAtUtc_NotOnOccurredOnUtc`
seeds the two clocks in **opposite** directions (seeded together, an implementation ordering on
either passes and the test proves nothing), and
`NoIndexTouchesOccurredOnUtc_AndTheClaimIndexSortsOnReceivedAtUtc` pins the model, because adding
the column to `IX_InboxMessages_Processable` costs no correctness, breaks no test, and makes the
column look like a legitimate sort key to whoever reads the schema next.

**`Source` was considered and deliberately not added.** Diagnostic and routing metadata with no
business semantics, already present in the receiver's log line and in the unbound-contract
exception. A column nothing reads is a migration everyone pays for. Recorded so the omission reads
as decided.

**3. An uncorrelated envelope fanned out into N unrelated chains.** With
`envelope.CorrelationId` null, the rows and the scope carried null, and each consumer that went on
to publish reached `OutboxMessageFactory.ResolveCorrelation`, which mints per staging call — so two
consumers of one delivery started two chains and neither reached back to the delivery. The seam is
the last point at which one id still covers the whole fan-out, so it mints there when the producer
had none. Copying is unchanged when the producer had one.

**4. A doubled `AddMessageHandler<,>()` read as a broker fault.** `MessageHandlerRegistry.AddEntry`
appended without deduplicating on `ConsumerType`, so one envelope fanned out twice to the same
consumer, the second write hit the dedup index, and a *first* delivery reported `Duplicates = 1`
while incrementing `microkit.inbox.messages.deduplicated` — the counter whose documented use is
detecting a short lease or a replaying broker. A composition typo presented as a steady dedup rate.
It now replaces rather than appends, so the two lookups cannot disagree about which invoker a
consumer type resolves to. `EnvelopeReceiver` and `InboxMetrics.Record` were also aligned: both now
refuse an unrecognised `InboxWriteResult` rather than one counting it as a row added and the other
counting nothing.

**Documented, not coded.** An ambient `System.Transactions.TransactionScope` around `ReceiveAsync`
defeats three things at once and reports none of them — the store inspects
`Database.CurrentTransaction`, which stays null under one, so the rows are not durable on return, no
savepoint is taken, and the `SupportsSavepoints` guard is bypassed. Noted on `IEnvelopeReceiver`'s
transaction-boundary paragraph, which previously ruled out only a transaction opened on the
`DbContext`. And `InboxPayloadException`'s "permanent until redeployed" is true of a *process* and
acted on by a *fleet*: during a rolling deployment an old instance dead-letters a message the
instance beside it would have accepted. The classification stays — it is right for a single-version
deployment, and nacking loops until the broker discards the message with no record of the refusal —
but the caveat is on the exception now, because the first broker provider inherits the rule
verbatim.

**Owed, not done.**

- **`OutboxProcessor.ResolveDispatcher`** — the F1 shape, uncorrected. See above.
- **No probe that a custom `IExecutionScopeFactory` bridged the context.** `OutboxProcessor` gets
  one incidentally by stamping `OriginMessageHolder` on the scope it is handed; the receiver has no
  equivalent, so a factory that ignores the `context` parameter writes rows into the default
  database silently in a DB-per-tenant deployment. Worth having, but it is a new structural check on
  the scope contract rather than a defect in this changeset — and it belongs on both processors and
  the receiver at once, not on one of them.
- **`EfInboxStore.AddAsync`'s savepoint rollback is unpinned.** Discovered while running the
  mutation checks below, not by the review. `EfIntegrationEventWriter` keeps **two** PostgreSQL
  cases — `WithAutoSavepoints` and `WithoutAutoSavepoints` — precisely because EF's automatic
  savepoints make the explicit one redundant when they are on, and load-bearing when a consumer sets
  `AutoSavepointsEnabled = false`. The inbox store carries the same reasoning in its own remarks but
  has only the auto-savepoints-on case (`InboxLeaseExpiryTests.Ambient_transaction_survives_a_deduplicated_write`),
  so deleting its `RollbackToSavepointAsync` turns **nothing** red. It needs the
  `WithoutAutoSavepoints` twin.

**Verified by mutation, five times.** The four from step 7 still hold; the new column adds the
fifth.

| Mutation | Expected | Observed |
|---|---|---|
| `ClaimToken` loses `IsConcurrencyToken()` | red | 1 red — `InboxLeaseExpiryTests.WhenTheLeaseExpiresMidHandler_…` |
| `EfIntegrationEventWriter` loses `RollbackToSavepointAsync` | red | 1 red — `AbsorbedDuplicate_…_WithoutAutoSavepoints` only, as designed |
| `continue` on `AlreadyPresent` → early return | red | 3 red, incl. `Redelivery_ThroughTheSeam_CostsNoConsumerItsRow` |
| Causation derivation → copy-through | red | 1 red — `ReceiveAsync_BuildsOneScopeCarryingTheTenantCorrelationAndDerivedCausation` |
| **Claim orders on `OccurredOnUtc`** | **red** | **1 red — `ClaimBatchAsync_OrdersOnReceivedAtUtc_NotOnOccurredOnUtc`** |

Each fix was also reverted and confirmed red: the catch-based `ResolveWriter`, per-row correlation
minting, and an appending `AddEntry`. The null-correlation revert **does not compile**, which is a
free second guard.

> ⚠ A caution for the next mutation run, learned the hard way here: restoring a mutated file with
> `shutil.copy2` preserves its mtime, MSBuild then considers the project up to date, and the *next*
> test run silently executes the previous mutation's binary. Restore content only and touch the
> file. One mutation result in this session was wrong until that was found.

Gate: Release, full suite, 396 passed / 0 failed / 0 skipped, including 19 PostgreSQL
Testcontainers tests. The api-reviewer has **not** run — next session.

#### Re-verification (2026-09-12) — same tree, measured without Docker and then with

The gate and the mutation checks were re-run from a clean session against the same working tree.
**No code changed**; this records what a second, independent run observed.

Gate: Release, build clean (0 warnings), **377 passed / 0 failed / 19 skipped**. The 19 skipped are
exactly the `[DockerRequiredFact]` set — no Docker daemon in the environment this time — and
377 + 19 = 396 reconciles with the run above.

| Check | Observed on re-run |
|---|---|
| `continue` on `AlreadyPresent` → early return | 3 red — `ReceivingSeamTests.Redelivery_ThroughTheSeam_CostsNoConsumerItsRow`, `…WhenOneConsumerIsADuplicate_StillWritesTheOthers`, `…WhenEveryConsumerIsADuplicate_ReportsItAndDoesNotThrow` |
| Causation derivation → copy-through | 1 red — `ReceiveAsync_BuildsOneScopeCarryingTheTenantCorrelationAndDerivedCausation` |
| Claim orders on `OccurredOnUtc` | 1 red — `ClaimBatchAsync_OrdersOnReceivedAtUtc_NotOnOccurredOnUtc` |
| Revert F1 → catch-based `ResolveWriter` | 1 red — `ReceiveAsync_WhenTheWriterCannotBeActivated_PropagatesRatherThanClassifying` |
| Revert F3 → per-row correlation minting | 1 red — `ReceiveAsync_WhenTheEnvelopeCarriesNoCorrelation_MintsOneForTheWholeFanOut` |
| Revert F4 → appending `AddEntry` | 2 red — `RegisteringTheSameConsumerTwice_ContributesOneEntry`, `ReRegisteringAConsumer_ReplacesTheEntryInBothLookups` |

##### The two PostgreSQL-pinned invariants, measured both ways

The remaining two mutations were run **twice on the same tree** — once with no Docker daemon, once
with one — so the claim that they are invisible to the fast suite is a measurement rather than an
assertion. Docker up: the full gate is **396 passed / 0 failed / 0 skipped**.

| Mutation | Without Docker (377 runnable) | With Docker (396 runnable) |
|---|---|---|
| `ClaimToken` loses `IsConcurrencyToken()` | **green — 377 passed, 0 red** | **1 red** — `InboxLeaseExpiryTests.WhenTheLeaseExpiresMidHandler_TheLoserRollsBackEntirelyAndTheWinnerStands` |
| `EfIntegrationEventWriter` loses `RollbackToSavepointAsync` | **green — 377 passed, 0 red** | **1 red** — `IntegrationEventPublishingPostgreSqlTests.AbsorbedDuplicate_LeavesTheCallersOwnWritesIntact_WithoutAutoSavepoints` |

Both failures are the *mechanism*, not a proxy for it. Dropping the concurrency token makes
`SaveChangesAsync` stop throwing `DbUpdateConcurrencyException` — the lost update the token exists
to prevent, arriving exactly as ADR-MSG-017 described it. Dropping the savepoint rollback fails with
Npgsql **`25P02: current transaction is aborted, commands ignored until end of transaction block`**
— the caller's transaction destroyed by an absorbed duplicate, which is the whole reason the
explicit savepoint is taken.

**`AbsorbedDuplicate_…_WithAutoSavepoints` stayed green under that second mutation.** That is the
paired observation that makes keeping both cases non-negotiable: with EF's automatic savepoints on,
the explicit one is redundant and its removal is invisible; with them off — an ordinary consumer
setting — it is load-bearing. One case alone reads as dead code.

**What this settles.** Two invariants of this module have exactly one test each standing between
them and a silent regression, and both of those tests are Docker-gated. A contributor who runs the
fast suite, sees 377 green and ships gets **no signal at all**. So neither line may be deleted on
the strength of a local run, and the Docker-backed suite is a release gate rather than an optional
extra.

**And it converts one owed item from asserted to measured.** The same mutation was applied to
`EfInboxStore.AddAsync`'s savepoint rollback, **with Docker up**, and turned **nothing** red —
396 passed / 0 failed / 0 skipped. So the two identical mechanisms are now separated by evidence
rather than by argument: the writer's line has a demonstrated red at full suite, the store's has
none at full suite. `EfIntegrationEventWriter` carries both auto-savepoint cases and
`EfInboxStore` carries only the auto-savepoints-**on** one
(`InboxLeaseExpiryTests.Ambient_transaction_survives_a_deduplicated_write`), where the explicit
savepoint is redundant. The `WithoutAutoSavepoints` twin is owed, and it is owed as a measured
debt — not because the code looks risky, but because a mutation of it was run and the suite did
not notice.

**Owed, carried forward:** the `WithoutAutoSavepoints` twin for `EfInboxStore`;
`OutboxProcessor.ResolveDispatcher` (the F1 shape, uncorrected); and the scope-contract probe (A5).
The api-reviewer still has **not** run — next session.

#### Implementation note — step 7 api-review application (2026-09-12)

The api-reviewer ran against the step-7 tree and its findings were applied in one pass. Nine
findings, one of which changed code; the rest were documentation, and the documentation half is not
incidental here — two of the three contracts this step adds (`ReceiveAsync`'s acknowledgement rule,
`InboxPayloadException`'s permanence) cannot be enforced from this repository at all, so their
statement *is* the enforcement.

**F1 — `InboxConfigurationException` meant one thing on the type and another in the assembly.**
Two halves, and only the second touched code.

*The type's remarks* described a single origin — the drain path — and a single mechanism, "raised by
wrapping the resolution call itself". Both were wrong the moment `EnvelopeReceiver` became a second
producer, and the remediation paragraph ("the processor settles the batch, releasing every row
untouched") describes machinery that does not exist at ingestion: no batch, no lease, no row.
Rewritten the way `OutboxConfigurationException` enumerates its origins on the type, so a reader is
not dependent on locating the call site: two origins, the remediation stated per origin, and the
detection mechanism stated once as `GetService` plus a null test.

*The assembly did not honour the narrowing the CHANGELOG claimed.* `InboxProcessor.Resolve` and
`InboxProcessor.ResolveSettlement` still caught `InvalidOperationException` around
`GetRequiredService`. `IInboxSettlementStore` resolves through a factory that activates
`EfInboxStore<TContext>` and with it the consumer's `DbContext`, so that catch spanned the whole
activation graph while reading as a registration lookup — the exact shape F1 of the previous review
removed from `ResolveWriter`. **It is worse on this path than at the seam**, which is why the
CHANGELOG claim was kept and the code moved to meet it rather than the reverse:
`InboxConfigurationException` abandons the batch and stops the drain worker, so under a tenant-aware
`IExecutionScopeFactory` one envelope naming an unknown tenant would have halted the inbox for
*every* tenant while reporting a registration that is present as missing. Both sites now
`GetService` + null test; an activation failure falls through to the per-message transient path and
costs one row one retry.

**Owed, and narrowed by one site that was not in the review.** `OutboxProcessor.ResolveDispatcher`
carries the same uncorrected shape — different exception type, different path, its own lot. While
applying this, **`OutboxProcessor.StampOrigin` was found to have it too**: it catches
`InvalidOperationException` around `GetRequiredService<OriginMessageHolder>()` and raises
`OutboxConfigurationException`. It is the lower-risk of the two (the holder is a plain class
registered by `AddMicroKitMessaging()` with no graph behind it, so there is little for a catch to
over-span), but it is the same reasoning and belongs in the same lot. Two sites owed, not one.

**F2 — the acknowledgement rule instructed the wrong reader.** It forbade an *ordering* of an ack
call, which is necessary but not sufficient, because the acknowledgement that loses a message is
usually one nobody writes. Restated as a constraint on the client's configuration — *nothing may
settle the broker message except code that runs after the returned task has completed* — with the
settling mechanism named per broker, mirroring what `IMessageTransport` already does on the send
side: Kafka `enable.auto.commit` (**on by default**, commits on a timer, can commit while
`ReceiveAsync` is in flight), RabbitMQ `autoAck: true`, Azure Service Bus
`ReceiveMode.ReceiveAndDelete`. `ServiceBusProcessor.AutoCompleteMessages` is named too, as
**safe** — it also defaults to true but completes after the message handler returns. The safe
default and the unsafe one both read as "auto"; naming only one would be worse than naming neither.

The conformance obligation was being delegated by cross-reference ("it carries the same conformance
obligation"). Now assigned, and explicitly **not** the same obligation: `SendAsync`'s asserts a
callee's return, this one asserts a caller's ordering, so it instruments the acknowledgement rather
than the seam — fail an `IEnvelopeReceiver` whose task does not complete until released, and assert
nothing has been settled at the broker meanwhile, against the real client configuration the provider
ships. Mirrored in the README, which had assigned the send-side test and not this one.

**F3 — registry idempotency was documented on a `private` member.** `AddEntry`'s reasoning was
sound and invisible: the behaviour change belongs to `Register`, `RegisterGeneric`, `GetHandlers`,
`RegisteredConsumerTypes` and `MessagingBuilder.AddMessageHandler<,>`, none of which said a second
registration is absorbed. Stated on all five, with the full consequence on `GetHandlers` (a doubled
entry makes a *first* delivery report `Duplicates = 1` on
`microkit.inbox.messages.deduplicated`, so a composition typo reads as a broker fault at a steady
rate, permanently).

**F4 — decision, recorded rather than left to be re-derived.** `InboxMetrics.Record` throws on an
unrecognised `InboxWriteResult` where it previously returned silently. That was chosen mid-lot and
**the throw stands**: `EnvelopeReceiver` classifies the same two cases, so recording nothing here
while the receiver counted the same write as a row added produces two counts of one write that
disagree, with neither flagged. A third enum value is this module changing an enum and missing a
call site, and the two sites must fail together or not at all. The cost is a metrics call that can
throw — accepted, because it is unreachable while the enum has its two documented values and the
alternative is a silent divergence. Now carries an `<exception>` tag saying so; it is a public
method with a public parameterless constructor, on a path `InboxIngestionLogs`' own docs invite
broker adapters onto.

**F5–F9.** `ReceiveAsync` gained an `<exception>` entry for the unclassified-`InboxWriteResult`
guard, which the catch-all rule was otherwise routing to redelivery — a module defect looping until
the broker's own limit. `EnvelopeReceiveResult.From` now rejects negatives, and the type records why
the factory is public (cross-assembly producer, not an invitation) and that `ConsumersMatched` being
derived **depends on** the F4 throw: a skipped consumer would leave the sum silently short, so the
coupling is load-bearing in both directions. `IEnvelopeReceiver` now names where it is registered,
and says plainly that a provider's assumption the seam is present is a convention rather than
something the container enforces. The rolling-deployment caveat is cross-referenced from the
`<exception>` block, which is what a provider author reads while writing the dead-letter branch.
And the receiver's class remark claimed it does not derive causation while deriving it three
paragraphs below — rewritten as the three statements that actually exist on the path (row copies,
receiver's scope derives, `InboxProcessor` derives again one hop later), because reading any one of
them as the rule for all three is the confusion that left `CausationId` null everywhere.

**A gap in the passing order itself, not a finding.** The surface list handed to the reviewer was
incomplete: it omitted `InboxMessage.OccurredOnUtc` and `InboxMetrics.RecordUnconsumed(string)`.
`OccurredOnUtc` is the consequential one — a new **non-nullable** property on a public EF entity,
so a required-column migration for anyone who has deployed the inbox table. The CHANGELOG documents
it correctly (migration SQL, backfill from `ReceivedAtUtc`, the no-index warning), so this was a
passing-order gap rather than an undocumented change. Recorded because **a member that forces a
migration is exactly what an API review exists to name**, and a reviewer given a list is reviewing
the list unless the list is complete. The lesson is procedural: derive the surface list
mechanically from the diff rather than from the changeset's narrative.

**Verified by mutation.** The two new tests mirror
`ReceiveAsync_WhenTheWriterCannotBeActivated_PropagatesRatherThanClassifying` and were confirmed to
discriminate, not merely to pass.

| Mutation | Expected | Observed |
|---|---|---|
| Revert F1 → catch-based `Resolve` and `ResolveSettlement` | red | **2 red** — `ProcessBatch_WhenSettlementStoreCannotBeActivated_PropagatesRatherThanClassifying`, `ProcessBatch_WhenHandlerCannotBeActivated_PropagatesRatherThanClassifying`. The two pre-existing `SettlesReleasedThenRethrows` tests stayed **green**, which is the other half: a genuinely absent registration must still be a configuration fault. |

Both new tests seed **`InvalidOperationException`** deliberately — the one type a catch-based
implementation swallows, so any other type would leave the regression free to return — and claim
**three** messages, because the discriminator is the batch's fate rather than one row's: a
catch-based implementation breaks the loop at index 0 and releases all three, so `Retried == 3` and
`Released == 0` is the assertion that says the fleet-wide halt is gone. Mutation applied, built, run
red, restored by content with an explicit `touch`, rebuilt, run green — per the mtime caution
above.

Gate: Release, build clean (**0 warnings**), **398 passed / 0 failed / 0 skipped**, Docker up,
including the 19 PostgreSQL Testcontainers tests. 396 + the two new tests = 398, so the baseline
reconciles exactly. No test was modified; two were added.

### Implementation note — outbox resolution classification: the container decides, the batch pays (2026-09-13)

Discharges the first bullet of the step-7 review closure's **Owed, not done** —
`OutboxProcessor.ResolveDispatcher`, "the F1 shape, uncorrected" — together with the second site the
api-review application note added to it, `OutboxProcessor.StampOrigin`. Both are now `GetService`
plus a null test.

**The model: three verdicts, decided by the container.**

| Container verdict | Classification | Effect |
|---|---|---|
| `GetService` returns **null** | configuration fault | batch released and settled, `OutboxConfigurationException` rethrown, worker stops — unchanged |
| `GetService` throws **`InvalidOperationException`** (not `ObjectDisposedException`) — how Microsoft DI reports an unsatisfiable dependency; a container that reports it with another type lands in the next row | batch-fatal, no budget | batch released and settled, **no rethrow**, no retry count, no dead-letter; `OutboxBatchAbortReason.DispatcherActivationFailed`, event 1009 at `Error`; the next cycle re-claims |
| `GetService` throws **any other type** | whatever the loop makes of that type | unchanged from HEAD — typed exceptions keep their arms, the rest is transient per message |
| anything thrown once the dispatcher exists | transient per message | unchanged |

No fourth nature of failure is introduced: the second row reuses the transport-unavailable arm's
shape — set the reason, break, release the remainder, do not rethrow — and adds one member to a
public enum.

**Per-message classification was rejected — for a cause every message shares.** The lot's first
passing order classified an activation failure as transient per message, mirroring the inbox, and was
withdrawn before implementation. For a missing `IMessageTransport`, classifying it per message
charges N retry budgets for one cause and, at `MaxRetries`, dead-letters the entire queue over one
line in a composition root. It would have turned "worker stops, rows `Pending`" into "every row,
notifications included, dead-lettered within a few cycles, recoverable only by requeue", and it would
have inverted `ProcessBatch_WhenNoTransportRegistered_ConsumesNoRetryBudget`, a test that names the
invariant by hand.

This note first grounded the rejection in a stronger premise: "a dispatcher is not state carried by a
message: if it cannot be built for message 1 it cannot be built for message 40". **That premise is
false.** `DispatchAsync` builds the scope from the row's `TenantId` and stamps its origin before
`ResolveDispatcher` runs, so activation can see the message, and a tenant-scoped cause fails for some
rows and not others. The rejection holds for a shared cause only; for the rest, see *Known defect*
below.

**The inbox/outbox asymmetry is not explained by who owns the failure.** `EnvelopeReceiver.ResolveWriter`
and `InboxProcessor.ResolveSettlement` let the same container verdict propagate. For the receiver the
difference is real: it holds one envelope and no batch, so a provider nacks one message. For the drain
processor it is not. `InboxProcessor` owns a claimed batch, a retry budget and `Released` outcomes
exactly as the outbox does; it retries the one row, and its `ResolveSettlement` remarks reject a
verdict that abandons the batch for this very cause, because under a tenant-aware scope factory it
would halt the inbox for every tenant. This note first argued that the outbox's batch-scoped remedy
followed from the outbox owning a batch and a retry budget, so the two sites only looked
inconsistent. **That argument is withdrawn**: it holds for `EnvelopeReceiver` only. The asymmetry
between the outbox and the drain processor stays in the code, and it is part of the known defect
below.

**Known defect — a batch-wide release that spends no retry budget stalls the queue for every tenant.
Fix owed.** This paragraph first recorded the stall as a decision, and as a property of dispatcher
activation: a deliberate liveness trade, preferred to the withdrawn draft's loss-by-dead-letter. The
distributed-context review challenged it, and it is **withdrawn as a decision**. Its second review, on
the narrowed premise, showed it is not specific to activation either: activation is one of three
routes to it. The verdict still ships because it improves on what it replaced for the
one input it covers — an `InvalidOperationException` during activation, on which the catch-based
resolver reported a missing registration and stopped the worker — but the stall is a defect, not a
design property. This sentence first said "on the same input" while the tagging catch covered every
type, where it was false: see *Only `InvalidOperationException` is tagged*, below.

- **The mechanism is the release, not the path that triggers it.** Both claims order on the row's own
  staging time — the outbox on `CreatedAtUtc` (`EfOutboxStore.cs:88`), the inbox on `ReceivedAtUtc`
  (`EfInboxStore.cs:212`) — and on both sides `Released` writes only `Status`, `LockedUntilUtc` and
  `ClaimToken`. A row released for a cause that never clears therefore stays eligible and stays oldest:
  it heads every claim, every batch aborts at it before anything behind it is processed, and the
  queue makes **no progress for any tenant** — on the outbox, notifications included. No budget
  moves, so there is no dead-letter exit, and neither admin store has an operation that steps past a
  row that is not dead-lettered.
- **Three routes reach it.**
  1. **Dispatcher activation.** A tenant-scoped `InvalidOperationException` while the dispatcher is
     built: `OutboxBatchAbortReason.DispatcherActivationFailed`, event 1009. New in this lot; at HEAD
     the same input stopped the worker.
  2. **A transport rejecting one tenant's credentials.** `IMessageTransport` names "authentication
     rejected" as a reason to throw `OutboxTransportUnavailableException`, and that arm releases the
     batch: `OutboxBatchAbortReason.TransportUnavailable`, event 1004. A provider holding per-tenant
     broker credentials meets it the moment one tenant's are revoked. The send path predates this lot.
     The constructor guidance this lot added to `IMessageTransport` and `AddTransportDispatcher()` —
     raise `OutboxTransportUnavailableException` when the broker is down — is right for a shared broker
     and silent on a per-tenant one.
  3. **The inbox analogue.** A handler throwing `InboxDependencyUnavailableException`, which names
     "authentication rejected" too, and whose arm releases the batch:
     `InboxBatchAbortReason.DependencyUnavailable`, event 2004. Pre-existing; this lot does not touch it.

  For all three, the parts are verified in code: the ordering, the release write, and a
  batch-breaking arm. The stall itself has been run on none of them.
- **Only the activation route can be traced from its event.** Event 1009 names the blocking row
  (`MessageId`, `TenantId`, `MessageKind`). **Events 1004 and 2004 carry no row identity** — their
  templates name a released count and nothing else — so an operator watching either cannot tell
  which tenant to act on. The row is still the oldest eligible one, since that is what heads the claim,
  so a query can find it. That follows from the claim predicate and has not been run.
- **The way out.** For both outbox routes, the README's SQL statement (*Sending to a broker*): mark the
  failing tenant's `Pending` rows dead-lettered, which takes them out of the claim. **It is not a way
  out of the inbox route.** It updates `"OutboxMessages"`, and the README documents no equivalent for
  `"InboxMessages"`.
- **It is reachable with the shipped dispatchers.** This note first said a tenant-scoped cause
  reaches the stall "only through a dispatcher with a tenant-scoped constructor dependency; the
  shipped dispatcher graphs are tenant-agnostic". **Overstated.** Activation runs in the message's
  scope, built from its `TenantId`. `MediatROutboxDispatcher`'s factory resolves `IPublisher` eagerly.
  That graph reaches `DomainEventsCascadeNotificationPublisher`, then `DomainEventDispatcher`, which
  materializes its sinks in its constructor, and through them `EfDomainEventsProvider<TContext>` and
  `OutboxDomainEventSink`'s `IOutboxWriter`: the consumer's `DbContext`. A host that resolves that
  context's connection per tenant, and throws `InvalidOperationException` for a de-provisioned
  tenant, fails right here and never recovers. The graphs are tenant-agnostic only while no host
  builds its `DbContext` per tenant, and nothing prevents one from doing so. (That the container picks MediatR's
  `Mediator(IServiceProvider, INotificationPublisher)` constructor is inferred from Microsoft DI's
  selection rule, not run.)
- **The dichotomy was false.** Liveness loss and loss-by-dead-letter are not the only options:
  deferring only the failing tenant's rows, spending no retry budget, loses neither. And
  dead-lettering has a supported exit, `RequeueAsync`; the stall has none.
- **The boundary is accidental, twice.** The same tenant-scoped cause thrown one line earlier, from
  `IExecutionScopeFactory.CreateScopeAsync`, is transient per message and dead-letters after
  `MaxRetries`. Whether a host's factory validates the tenant eagerly or lazily decides between a
  dead-letter after `MaxRetries` attempts and a stall with no end. Since the narrowing, the exception
  type the host throws decides the same thing: a lookup failing with `KeyNotFoundException` is
  retried per message while every other tenant publishes; failing with `InvalidOperationException`,
  it stalls them all.
- **Mitigated in this lot, not fixed, and only on one route.** On the activation route, event 1009
  names the blocking row (`MessageId`, `TenantId`, `MessageKind`), so an operator can find it. The
  transport and inbox routes gained nothing. Nothing is lost on any route.
- **Owed, each its own lot:** deferral of the failing tenant's rows without spending retry budget; an
  admin operation to park or dead-letter a `Pending` row; an outbox metric (age of the oldest eligible
  row, aborted batches by reason); and a decision on where tenant validation belongs, scope creation or
  dispatcher activation, written into the custom `IExecutionScopeFactory` contract.
- **Two constraints travel with the deferral lot** (api-review N1, N5).
  - Deferral takes its **own event id** rather than changing what 1009 means. 1009 is new in this
    cycle, its text ("Abandoning batch and releasing …") becomes false for the case deferral fixes,
    and operators may match on it. The stable surface is the event id and the property names
    `MessageId`, `TenantId`, `MessageKind` and `ReleasedCount`, not the text; `EventName` follows the
    method name, so renaming `DispatcherActivationFailed` would change it silently.
  - Its done list rewrites every site that documents the stall:
    `OutboxBatchAbortReason.DispatcherActivationFailed`, `OutboxConfigurationException`,
    `MessagingBuilder.AddTransportDispatcher()`, `OutboxProcessor` (`ResolveDispatcher` and the
    activation arm), event 1009's remarks, the README section, the CHANGELOG entry, the outbox-inbox
    rule, the testing rule, and test 1.

**The worker needed a cadence arm, or the new reason would spin.** `OutboxBatchResult.IsSaturated`
ignores the abort reason, and `OutboxWorker.NextDelay` tested only `TransportUnavailable` and
`Cancelled` before saturation. A full abandoned batch therefore polled again at zero delay and
re-claimed the rows it had just released, failing the same way in a tight loop.
`DispatcherActivationFailed` now shares the transport-outage arm (`TransportUnavailableBackoff`),
pinned on a saturated result.

**How the loop recognises the verdict.** `ResolveDispatcher` keeps one `try`, around `GetService`
alone, and rethrows the `InvalidOperationException` it catches as the internal
`OutboxDispatcherActivationException`, which the loop catches by type. `GetService` never throws for
an absent registration, so the wrapper *tags* an activation failure and classifies nothing as
configuration; the null test alone produces `OutboxConfigurationException`. A bare
`catch (InvalidOperationException)` in the loop was rejected: a dispatcher's own `DispatchAsync`
throws that type as a genuine per-message fault. The wrapper derives from `Exception`, not
`InvalidOperationException`, so no such catch can capture it.

**Only `InvalidOperationException` is tagged — narrowed after the api-review (2026-09-13).** The
tagging catch first covered every type except `OperationCanceledException` and
`ObjectDisposedException`. The api-reviewer blocked on it (B2), and the finding holds against HEAD:
HEAD's resolver caught `InvalidOperationException` alone, so any other type thrown during activation
reached the loop's `catch (Exception)`, was retried per message and dead-lettered at `MaxRetries`,
while the rest of the batch — every other tenant — carried on. The wide catch moved that case into
the batch-wide verdict. A tenant lookup failing with `KeyNotFoundException` for a de-provisioned
tenant went from "that tenant dead-letters" to "no tenant publishes", and this note recorded it as an
improvement. It also cost typed exceptions their arms (S2): an `OutboxTransportUnavailableException`
from a transport's constructor would have logged 1009 instead of 1004, and an
`OutboxConfigurationException` from a constructor would no longer have stopped the worker.

The fix narrows the catch rather than documenting the widening:
`catch (InvalidOperationException ex) when (ex is not ObjectDisposedException)`. The lot's mandate was
to separate two verdicts the container — Microsoft DI, which is all that was probed — conflates under **one** type — "not registered" and
"registered, cannot be activated", both read as `InvalidOperationException` by the catch-based form.
Nothing else was ever conflated, so nothing else moves.

- **B2 and S2 collapse.** Every type but `InvalidOperationException` keeps HEAD's classification.
- **The main case is unaffected.** Microsoft DI reports an unsatisfiable constructor dependency as a
  raw `InvalidOperationException`, whether the dispatcher is reached through the keyed forwarder or
  through a decorator's `GetKeyedService`, and wraps nothing it did not throw itself: a
  `KeyNotFoundException` from a constructor, a nested constructor or a factory reaches the caller
  unwrapped. Both were run in a scratch probe on the net10.0 shared framework; the first is also what
  keeps the no-transport tests green on a real container.
- **The known defect narrows; it does not close.** A tenant-scoped cause still stalls the outbox
  when it surfaces as `InvalidOperationException`, the idiomatic type for "no connection string is
  configured for this tenant".
- **The win is Microsoft DI's.** A container that reports an unsatisfiable dependency with another
  type keeps HEAD's per-message verdict for a missing transport. Inferred from the type test; no
  third-party container was run.
- **Pinned** by
  `ProcessBatch_WhenActivationThrowsAnythingButInvalidOperationException_RetriesOnlyThatMessage`:
  test 1's fixture with a `KeyNotFoundException` — message 2 retried, message 3 published, no abort
  reason, no 1009. M10 below restores the wide catch against it.

**Shutdown is not activation.** `OperationCanceledException` does not derive from
`InvalidOperationException`, so the tagging catch never sees it: it reaches the loop's cancellation
arm when the token is cancelled, and the transient arm otherwise, exactly as at HEAD.
`ObjectDisposedException` does derive from it, and the filter excludes it. Not being an
`OperationCanceledException`, it can never reach the cancellation arm: it reaches the transient arm and
costs the message it hit one retry — which is what the same exception from `CreateScopeAsync` already
did. Since the narrowing, the cancellation test pins the type hierarchy rather than a term of the
filter.

**`StampOrigin`: same idiom, unchanged behaviour, half unpinnable.** Its exception message is kept
verbatim, and it has no activation arm: `OriginMessageHolder` is a dependency-free internal marker, so
its activation cannot fail, and its remarks say the method must be revisited if that changes. The
change of *form* from catch to null check is unpinnable — no reachable input tells them apart — but the
*null branch* is pinned: removing it turns both `WhenTheScopeCannotSupplyTheOriginHolder` tests red.

**Test 1's fixture fails activation for one message, and that shape is realistic.**
`ProcessBatch_WhenTheDispatcherCannotBeActivated_ReleasesTheBatchWithoutConsumingRetriesOrRethrowing`
fails activation for message 2 only, which is what a tenant-scoped cause does. This note first called
the fixture deliberately unrealistic, on the premise that the model admits no per-message activation
failure; that premise is false (see *Per-message classification*). Failing one message is also the
only fixture that tells batch-wide handling apart from per-message handling: a per-message
implementation retries message 2 and publishes message 3. The test pins today's verdict, the known
defect included (message 3 is released for message 2's cause), so the owed fix is expected to change
it. Since the review it also asserts that event 1009 names message 2's id, tenant and kind, with
message 2 given its own tenant and kind so the assertion cannot pass on a neighbour's values. And the
batch's outcomes, `[Published, Released, Released]`, do **not** discriminate against the catch-based
implementation this replaced, which produced the same three. Against that one, the discriminators are
the absent rethrow, the abort reason, and event 1009 where it logged 1006.

**Owed, carried forward.**

- **The four worker sites.** `OutboxWorker.cs:64`, `InboxWorker.cs:64`, `OutboxRetentionWorker.cs:69`
  and `InboxRetentionWorker.cs:77` each catch `InvalidOperationException` around `GetRequiredService`
  of a service whose graph reaches the consumer's `DbContext`, report any activation failure as a
  missing registration, and stop the worker. At worker level an activation failure stops the worker
  either way, so the right verdict there is not obviously this one. Its own lot, decided cold.
- **The tagging filter outside a shutdown.** `ResolveDispatcher` excludes `ObjectDisposedException`
  whether or not the processor's token is cancelled, and the loop's cancellation arm matches only a
  cancelled token, so outside a shutdown it reaches the transient arm. The catch-based resolver caught
  it as an `InvalidOperationException`, reported a configuration fault and stopped the worker, so this
  lot has already moved it to *retried*. A non-shutdown `OperationCanceledException` — a factory
  timeout surfacing as `TaskCanceledException` — is transient too, is charged per message and can
  dead-letter the queue, but not because of this lot: it is not an `InvalidOperationException`, and no
  form of the resolver tagged it, at HEAD, under the wide catch, or since the narrowing. Tagging
  either when the token is not cancelled would move it to the batch-wide activation verdict. Raised by
  the distributed-context review and declined here as a classification decision this lot did not set
  out to take; its own lot, beside the worker sites.
- **The shutdown retry charge.** "A shutdown-time ObjectDisposedException from either
  CreateScopeAsync or dispatcher activation is charged to the message's retry budget. Deciding
  whether shutdown should release rather than retry is a change to the :150 arm and is owed." (`:150`
  is the `OperationCanceledException` arm of `ProcessBatchAsync` as the lot found it.)
- Unchanged from the step-7 closure: the custom-factory context probe, and `EfInboxStore.AddAsync`'s
  `WithoutAutoSavepoints` twin.

**Verified by mutation — fifteen mutations, re-run in full after the narrowing.** Eleven were written
before the distributed-context review and M12–M12c after it, one per value event 1009 names; after
the api-review, M10 was replaced and M10b added. M9 removes the whole call, so it cannot tell a right
value from a wrong one; M12–M12c each replace one value with what a wrong row in test 1's batch would
carry, and none stayed green. Each mutation was applied by content, built in Release, run against the
full suite (404 tests, Docker up), then restored by content and touched — the mtime caution above.
Every run checked for an injected marker comment and a rebuilt `MicroKit.Messaging.dll`; both mutated
files were restored to their pristine sha256, and the clean gate after them passed 404. The re-run
re-derived M1–M11 from the descriptions below, because the first runner's script was not kept. Every
count but M10's matches the first run's.

| # | Mutation | Expected | Observed |
|---|---|---|---|
| M1 | `ResolveDispatcher` back to `GetRequiredService` + `catch (InvalidOperationException)` → `OutboxConfigurationException` | red — test 1, the no-transport test | **4 red** — `…WhenTheDispatcherCannotBeActivated_…`, both `…WhenNoTransportRegistered_…`, and `…WhenTheProviderIsDisposedDuringActivation_…` |
| M2 | Null check removed (`GetService<IOutboxDispatcher>()!`) | red — `WhenDispatcherUnregistered` ×2 | **2 red** — both `…WhenDispatcherUnregistered_…` |
| M3 | Activation arm classified per message (`BuildTransientOutcome`, no `break`) | red — test 1 | **3 red** — test 1 and both `…WhenNoTransportRegistered_…` |
| M4 | The naive form: no wrapping for `InvalidOperationException`, the arm a bare catch on it | red — test 2; **test 1 green** | **6 red, test 1 green** — `…WhenTheDispatcherItselfThrowsInvalidOperationException_…`, `…WhenTheProviderIsDisposedDuringActivation_…`, and four pre-existing per-message tests (`…DispatchThrows_BelowThreshold…`, `…DispatchThrows_AtThreshold…`, `…ErrorMessageExceedsLimit…`, `…WhenTransportFailsUnclassified…`) |
| M5 | `StampOrigin` null check removed | red ×2 | **2 red** — both `…WhenTheScopeCannotSupplyTheOriginHolder_…` |
| M6 | `StampOrigin` back to the catch form, exception message verbatim | **green, expected** | **green — 404 passed.** No reachable input distinguishes the forms. The re-run's first attempt also replaced the message and went red on the test asserting it names `OriginMessageHolder` — a runner fault, corrected and re-run |
| M7 | `DispatcherActivationFailed` removed from the `NextDelay` arm | red | **1 red** — `NextDelay_WhenTheDispatcherCannotBeActivated_BacksOffTowardTheOutageCeiling` |
| M8 | Activation arm also assigns `configurationFault`, so it rethrows | red — test 1, the no-transport test | **3 red** — test 1 and both `…WhenNoTransportRegistered_…` |
| M9 | Event 1009 log call removed | red — test 1 | **1 red** — test 1 |
| M10 | The tagging catch widened back to `Exception` minus `OperationCanceledException` and `ObjectDisposedException` — the form before the narrowing. Replaces the first M10, which dropped `OperationCanceledException` from a filter term that no longer exists | red — test 5 | **1 red** — `…WhenActivationThrowsAnythingButInvalidOperationException_…` |
| M10b | Widened to `Exception` minus `ObjectDisposedException` only — cancellation forgotten | red — tests 3 and 5 | **2 red** — `…WhenDispatcherActivationIsCancelled_…` and `…WhenActivationThrowsAnythingButInvalidOperationException_…` |
| M11 | `ObjectDisposedException` no longer excluded (`catch (InvalidOperationException ex)`, no filter) | red | **1 red** — `…WhenTheProviderIsDisposedDuringActivation_…` |
| M12 | The activation arm's 1009 call logs `messages[0].Id.Value` (the batch's first message) instead of `message.Id.Value` — added after the review | red — test 1 | **1 red** — `…WhenTheDispatcherCannotBeActivated_…` |
| M12b | The same call logs the constant `"tenant-a"` (the neighbours' tenant) instead of `message.TenantId` | red — test 1 | **1 red** — test 1 |
| M12c | The same call logs `MessageKind.Notification` (the neighbours' kind) instead of `message.MessageKind` | red — test 1 | **1 red** — test 1 |

Four observations beyond the expectations:

- **`…WhenNoTransportRegistered_ConsumesNoRetryBudget` now guards the rethrow as well as the budget.**
  Its `Should.ThrowAsync` wrapper was the one line this lot removed from it. Called directly, it fails
  under M1 and M8 because the call throws, so the edit made it a second guard rather than a weaker one.
- **M1 also reddens the disposal test.** A catch-based resolver catches `ObjectDisposedException`, because
  that type *is* an `InvalidOperationException`. It would report a clean shutdown as a missing
  registration and stop the worker.
- **M4 reaches four older tests.** They all throw `InvalidOperationException` from a dispatch or a
  transport, and a bare catch in the loop would have released their batches instead of retrying.
  `…WhenTheDispatcherItselfThrowsInvalidOperationException_…` is the only one whose name states the
  property. Test 1 stays green under M4, which is exactly why test 1 alone cannot rule out that form.
- **Test 5 stays green under M4, and that is correct.** The naive form wraps nothing a
  `KeyNotFoundException` could reach, so that type takes the transient arm there exactly as it does
  in the narrowed code. Test 5 separates the narrowed catch from a *wider* one (M10, M10b); it was
  never meant to catch the naive form, which test 2 does.

Gate: Release, build clean (**0 warnings**), **404 passed / 0 failed / 0 skipped** (43 · 21 · 233 · 8 ·
99), Docker 29.7.2 up, so the 19 PostgreSQL Testcontainers tests ran inside the full suite. The
separate filtered run (19 passed) was not repeated after the narrowing. The gate ran on the final
bytes, after a reflow of four remarks and the M6 re-run. It ran once more after the second
specialist review's documentation edits, with the same result and no compiled input changed.

- The baseline, measured before any edit, was 398. 398 plus the six new tests gives 404; the rename
  leaves the count unchanged.
- One test was renamed and inverted: `…SettlesEveryMessageReleasedWithoutRethrowing`.
- One test had its `ThrowAsync` wrapper removed: `…ConsumesNoRetryBudget`.
- The distributed-context specialist has run (2026-09-13). The corrected premise, the withdrawn
  asymmetry argument, the *Known defect* paragraph and event 1009's row fields above are its findings;
  the tagging-filter item under *Owed* is one it raised and this lot declined.
- The api-reviewer has run (2026-09-13): BLOCK on B1 and B2. B2 is resolved by the narrowing above
  rather than by documenting the widening. B1, S1–S3 and the notes are applied to the enum member,
  `OutboxBatchResult.IsSaturated`, `AddTransportDispatcher()`, `OutboxConfigurationException`,
  `IMessageTransport` and the README (`IS NULL` variant, `ValidateOnBuild`); S2 collapsed with B2.
- The distributed-context specialist has run again (2026-09-13), on the narrowed premise: stands
  with amendments, no BLOCK. It found that the stall is not specific to activation (*Known defect*,
  three routes) and that the `InvalidOperationException` claim is Microsoft DI's (the verdict table
  and the narrowing paragraph now say so). Its constraints on the deferral lot, the XML-doc
  qualifier it scoped to the next api-reviewed lot, and its notes are carried in trace 029, §7
  and §10.

---

## ADR-MSG-020: Opt-In Durability on the Bus — Stores, the Inbox Guard and the Local Endpoint

**Status:** Accepted
**Date:** 2026-11-21
**Issue:** #188
**Supersedes:** ADR-MSG-013. **Supersedes (in part):** ADR-MSG-017 (the transaction model), ADR-MSG-018 §4, ADR-MSG-019 §1 and §7. **Amends:** ADR-MSG-016. **Amended by:** ADR-MSG-021 (§2, §8).

### Context

#186 made the bus publish and consume with no persistence. The outbox and the inbox were still a
separate API — `IIntegrationEventPublisher`, which required a transaction the caller opened, and an
inbox fed by ingestion and drained by a processor that settled in four transactions — and one
`DbContext` per host. Domain-event notifications were fanned out by a MediatR decorator with no
per-handler inbox, so a replay re-ran every handler, and cascade events were staged and never saved.

### Decision

1. **A store is opt-in durability of the bus**, the outbox and the inbox of one `DbContext`,
   configured with `bus.UseEntityFrameworkOutbox<TContext>()`; a host may have several. One
   publication API: `IMessageBus` / `IEventPublisher.PublishAsync`.
2. **A store SPI** (`MessageStoreKey`, `IMessageStoreSet`, `AddMessageStore`, `MessageStoreBuilder`):
   the EF store is built on it alone; the store set is static here, dynamic in #191.
3. **Store resolution, first match:** `PublishOptions.Store`; the delivery's store; the only store;
   the store bound to the message type's assembly; else a configuration exception. A handler's store:
   the only one, else its assembly's binding, checked at start. The instance and key always come
   from the scope.
4. **Outbox by enlistment:** the row is added to the store's `DbContext` of the scope and never
   saved by the bus; a row unsaved at scope end is reported (4000), one discarded by a tracker clear
   at `Debug` (4001). The replay key is removed.
5. **The inbox guard, on one receive pipeline** for transport deliveries, local deliveries and
   retries: one row per (message, handler); an existing row skips a new delivery; an attempt is one
   operation of the store's execution strategy, its row, the handler's work, its publications and
   its cascade events committing together, an unknown commit verified by reading the row; a failure
   rolls back and is written on the row in its own transaction before the delivery is acknowledged;
   the store's loop owns the retries. Exactly-once covers what is written through the store's
   context; everything else is at-least-once. Two amendments to the issue's text: the scope
   initializers run before the attempt's transaction begins (they configure the scope the store's
   context is built from), and a permanent verdict counts no attempt (a first delivery is written
   dead-lettered, `RetryCount` 0).
6. **The local endpoint:** an in-process message never handed to a transport. With a store, an
   outbox row routed `Endpoint = "local"`, drained by the store's loop into the pipeline, one guarded
   delivery per consumer; without one, an in-memory queue of one entry per (message, consumer),
   each with its own retries — queued before the sender's work commits, and lost if the process stops.
7. **The MediatR bridge** (`bus.UseMediatRDomainEvents()`) on the public SPI: a sink that sends
   through `ILocalMessageSender`; each `INotificationHandler` a consumer of its own; a receive
   middleware that dispatches cascade events before the engine's save. A notification handler that
   cannot be a consumer of its own is a start failure, never a silent skip.
8. **One engine loop per store** (outbox, inbox retries, their retention), each with its own
   back-off; a host stop releases what it claimed with attempts unchanged; an activation failure is
   reported as such, never as "not registered". Within a store's outbox, **each endpoint has its own
   back-off**: a row whose endpoint is down — its transport unreachable or absent, a local consumer's
   dependency out — is deferred to the end of that back-off with no attempt counted, and the claim
   leaves the endpoint's rows out while it runs (`IOutboxProcessorStore.ClaimBatchAsync` takes
   `OutboxEndpoints excluded` in its one signature, with no default implementation: a store that
   ignored it would starve the local rows during a broker outage), so the other endpoint's rows keep flowing, the down endpoint's are not
   rewritten, and it is tried once per back-off period. Deferral has its own event, 1010, as
   ADR-MSG-019 required; 1004 and 1009 keep their event names and say why the endpoint is down. They
   lose `ReleasedCount` — an endpoint outage releases nothing any more (cancellation, a host stop and
   a configuration fault still release, and say so in their own events) — and 1009 carries
   `Endpoint` where it carried `MessageKind`. The claim's endpoint filter has its own index,
   `IX_OutboxMessages_Endpoint_Dispatchable`, measured on PostgreSQL.

### Consequences

- `IIntegrationEventPublisher`, `IIntegrationEventWriter`, `IntegrationEventWriteResult`,
  `IInboxWriter`, `IInboxSettlementStore`, `InboxWriteResult`, `OutboxInboxProcessingExtensions`,
  `MessageKind`, `OutboxDispatcherKeys`, `MediatROutboxDispatcher` and
  `DomainEventsCascadeNotificationPublisher` are deleted; `IOutboxDispatcher` is internal. The
  glue has no access to Core's internals.
- Schema: `OutboxMessages` loses `MessageKind`, `OriginMessageId` and the replay index, and gains
  `Endpoint` and `Headers`; `InboxMessages` gains `Source` and `Headers`. Hosts own the migration,
  in the CHANGELOG — `Endpoint` must be set from `MessageKind` before it is dropped.
- A notification handler, a bus handler's cascade and a retry all run under the same guard, so the
  in-process path no longer re-runs a handler that succeeded.
- Prior art. NServiceBus's outbox stores the incoming message id, the outgoing messages and the
  business state in one transaction, on the handler's own connection, and excludes from its
  guarantee whatever does not enlist in it (<https://docs.particular.net/nservicebus/outbox/>) —
  the split of decision 5. MassTransit's EF Core outbox tracks an `InboxState` per received message
  for each endpoint (<https://masstransit.io/documentation/patterns/transactional-outbox>). Both
  deduplicate per message and endpoint; this module keys the inbox per handler, so one handler that
  fails is retried alone while the others of the message are done.

---

## ADR-MSG-021: Partitioned Stores — the Engine Drains Store Instances Supplied at Runtime

**Status:** Accepted
**Date:** 2026-11-22
**Issue:** #191
**Amends:** ADR-MSG-020 §2 (the store set) and §8 (one engine loop per store).

### Context

A store (ADR-MSG-020) is the outbox and the inbox of one `DbContext`, fixed when the host starts. A
database-per-tenant application — or any topology with several databases behind one model, shards
included — had no outbox drain for its tenant databases: nothing iterated them, added one at runtime,
isolated their failures or shared work fairly between them. `IMessageStoreSet` had three readers with
two meanings: what is drained (the engine) and what is configured (store resolution, handler binding,
start checks, the MediatR bridge). A dynamic set read through it would make a host with a configured
store look storeless whenever it had no database yet.

### Decision

1. **Store and store instance, kept apart.** A *store* is what a host configures — `AddMessageStore`,
   its `MessageStoreKey`, its keyed services — static, fixed at start; `IMessageStoreSet` keeps that
   meaning, and its readers are unchanged. A *store instance* (`MessageStoreInstance`) is a unit of
   persistence the engine drains: one database of a store. A store is **single** — one instance, the
   store itself: today's behaviour, keys, tags and log series — or **partitioned**
   (`MessageStoreBuilder.PartitionedBy<TSource, TInitializer>()`), its instances supplied at runtime.
   The SPI names no tenant, no `DbContext` and no technology.
2. **The instance source** (`IMessageStoreInstanceSource`): a snapshot (`GetInstances()`) and a change
   token (`GetChangeToken()`), the shape of ASP.NET Core's `EndpointDataSource` and of options
   monitoring. The engine reconciles from the snapshot on each change, so a missed or coalesced signal
   loses nothing; signals are coalesced in a bounded channel of one that drops a write while one is
   pending, so the token's callback, which runs in the source's code, never throws. A snapshot that
   throws or holds one id twice is logged (1407, 1408) and changes nothing.
3. **The instance scope.** Every scope opened for an instance — the four loops' scopes, the scope each
   outbox row is dispatched in, a delivery's and each attempt's — names the store and the instance,
   then runs the store's `IMessageStoreInstanceInitializer`, before any of its services is resolved; in
   a message scope, before the `IMessageScopeInitializer`s. Loop scopes run the instance initializer
   only. The processors, the guard and the back-off state of the scope are the instance's.
4. **One back-off state per instance, deliveries included.** A delivery guarded by a partitioned store
   is mapped to its instance — a local row's own instance, else the source's `TryGetInstance` — so its
   back-off decision and the retry loop of the instance that holds its row read one state. The lookup
   never creates state: only the engine creates and releases an instance's. A delivery that maps to no
   instance drained here is a counted failure (`DeliveryVerdict.Transient`, 1410), redelivered and
   dead-lettered at the transport's limit, never a pause: pausing would hold every tenant of the
   endpoint behind it, the stall #124 removed.
5. **Lifecycle.** Added: its state is created and its four loops start (1404). Removed: each loop
   settles the batch it holds — never cancelled — then stops claiming (1405); its state and metric
   series are released once its loops have ended (1406). Re-added meanwhile, it starts again only then.
   Host stop is unchanged. A removed instance's rows stay where they are.
6. **One FIFO gate** (`StoreInstanceOptions.MaxConcurrentBatches`, 16) bounds the outbox and inbox
   batches in progress, claim to settlement, across every instance of every partitioned store, and
   admits waiters in arrival order: a released permit is handed to the oldest waiter, never to a
   newcomer, so an instance with a large backlog cannot starve another. `SemaphoreSlim` documents no
   waiter order; `ConcurrencyLimiter` does (`QueueProcessingOrder.OldestFirst`) but lives in a package
   outside the shared framework. A release and a cancellation are arbitrated under the gate's lock by
   the waiter's presence in the queue. **Single stores never enter the gate.** The gate bounds
   batches, not connections: a host with a database per tenant sizes its pools or uses a pooler.
7. **Start checks.** Single stores are checked at start as before. A partitioned store's instance
   checks (`MessageStoreBuilder.AddInstanceCheck`; the EF model check) run once, in the prepared
   scope of the first instance to start: problems are the store's — logged Critical (1409), no
   instance of it drains until a restart, its deliveries get §4's verdict — and a throw is the
   instance's, which backs off on its own loops while the check stays pending.
8. **No store initializer.** No table is created, no migration runs: a source declares an instance once
   its database is provisioned. Hosts own their migrations.
9. **Metrics.** `store.key` stays the store's key on every instrument, bounded by the configured stores.
   `StoreInstanceOptions.TagInstances` (off) adds `store.instance` to every instrument tagged
   `store.key`; off, a partitioned store reports one series per (`store.key`, `endpoint`), its back-off
   counters summed over its instances and zero while it has none. A single store's series are unchanged.

Instance loops log their own events (1411–1414) with `{Store}` and `{Instance}`; 1400–1402 stay a
single store's. Each iteration of an instance's loop runs in a logging scope carrying both, so a
processor's events (1011, 2013, …) name the database that failed.

### Consequences

- `IMessageStoreSet` and its readers are unchanged; ADR-MSG-020 §2's "dynamic in #191" is this ADR,
  and §8's "one loop per store" reads "per store or per instance".
- Out of scope, each owned by an issue: one engine instance per store and the lease that would move
  ownership between hosts (#214) — correctness rests on the atomic claim, as before; waking a loop on
  commit (#215 — amended by ADR-MSG-022); the Tenancy adapter and its source (#189); tenant connection
  resolution (#190).
- Prior art. Wolverine runs one durability agent per tenant database, assigned by its leader to one
  node, discovers tenant databases from a master table or a custom source, routes by the envelope's
  tenant, and creates the message tables itself
  (<https://wolverinefx.net/guide/durability/postgresql.html>,
  <https://wolverinefx.net/guide/durability/efcore/multi-tenancy.html>). This module keeps the source
  and the routing, leaves assignment to #214, documents no table creation, and adds a FIFO bound
  across databases that Wolverine does not document.

---

## ADR-MSG-022: A Commit Wakes Its Store's Outbox Loop

**Status:** Accepted
**Date:** 2026-11-24
**Issue:** #215
**Amends:** ADR-MSG-021 (its out-of-scope "waking a loop on commit").

### Context

An idle outbox loop grows its delay toward `MaxPollingInterval`, 1 minute by default, and nothing told
it a row was committed: after a quiet period, a store's first publication — and its first local
delivery, a row of the same loop (ADR-MSG-020 §6) — waited up to that long. Lowering the ceiling trades
that latency for a round trip per store, or per instance, per interval.

### Decision

1. **When.** A store provider reports a commit at the end of the publication's scope. The EF store does
   it in `Dispose`, after its unsaved-row report, once, when a row it enlisted is `Unchanged` and the
   scope's guarded inbox attempt did not stay uncommitted; never when the context was already disposed.
   The container disposes in reverse order of creation and the store takes its context through its
   constructor, so a transaction the context owns is committed by then, or about to roll back with the
   context. No EF interceptor: `SavedChanges` runs before an explicit transaction commits, so its
   signal would precede the row's visibility and be lost.
2. **Contract.** `IOutboxCommitSignal.Committed(MessageStoreKey)`, in the store SPI, implemented by the
   engine and registered with the first store, scoped. A provider resolves it at its first enlistment —
   a disposing scope resolves nothing — and calls it once its rows are visible. It never throws and never
   blocks; a scope that enlisted nothing resolves nothing for it.
3. **What is woken.** A single store's outbox loop; for a partitioned store, the instance the scope's
   `DeliveryStoreHolder` names for that store. Nothing otherwise — a partitioned store from a host scope
   or from another store's delivery scope, an unknown key, a removed instance — and no call to the
   instance source, which maps a delivery, not a publication.
4. **Signal.** One per outbox loop, held with the state that outlives cycles: a frozen map built once for
   the single stores, the instance's state for an instance. A bounded channel of one, `DropWrite`,
   asynchronous continuations, written with `TryWrite`, never completed: commits coalesce, and the loop
   never resumes on the committing thread.
5. **Drained before the claim.** The loop drains the signal right before each claim, after the gate, never
   after the batch: a commit landing during a batch leaves its signal pending, and the next wait ends at
   the floor.
6. **What a wake cuts.** Only the delay after an empty or a partial batch. Never the hand-over guard, a
   cancelled batch, a preparation, activation or configuration fault's back-off, or a failed cycle's;
   endpoint and tenant back-offs are untouched, the claim still excluding them (#124). An empty claim
   after a wake grows the delay from where it was. Loops given no wake — inbox, retention, the in-memory
   transport, the local queue — wait as before.
7. **Floor.** A wake starts a claim no sooner than `PollingInterval` after the previous claim began, to
   the timer's precision, read on the loop's clock. During a transport outage commits keep arriving, and
   each empty claim reads the down endpoint's backlog (#124's measured bound): with the floor, the cost is
   at most the partial-batch cadence the loop already has. After a quiet period the previous claim is
   long past, and the wake is immediate.
8. **No log, metric or option.** A wake is not an event worth a line per scope.

### Consequences

- Limits, documented, not handled: a transaction owned outside the scope (`UseTransaction` with a
  transaction committed after the scope, or an ambient `TransactionScope` completed after it) is
  signalled too early, and its row waits for the next poll; a row saved and then rolled back by the
  context's transaction costs one empty claim; the EF store reads each row's state when the scope ends,
  so a row saved and then detached by a clear of the tracker, and a guarded attempt whose commit landed
  with an unknown outcome (its scope ends before the verification), are not woken; a commit in a scope
  prepared for an instance since removed and re-added does not wake its successor; a publication from a
  host scope to a partitioned store is not woken (#189); another host is not woken (#214, or a broker
  transport, #134).
- A wake only saves latency: the loop still polls, so a missed or early wake delays a row to the next
  poll, never loses it.
- Prior art. MassTransit's `EntityFrameworkScopedBusContext.Dispose` calls
  `IBusOutboxNotification.Delivered()` when its outbox state entry is `Unchanged` (MassTransit `d9f61f7`,
  `EntityFrameworkScopedBusContext.cs:51-57, 96-99`); its notification is dropped when no wait is
  pending (`BusOutboxNotification.cs:20-48`), where this signal stays pending. CAP (`a4f8cd3`) and
  Wolverine (`6aff3e0`) send from memory after a commit made through their own API; this module
  publishes by enlistment and never commits the host's work (#188), so that model does not apply.
  Disposal order: Microsoft Learn, "Dependency injection scope disposal aggregates exceptions" (reverse
  capture order); a disposing scope refuses to resolve (dotnet/runtime `2ba107f`,
  `ServiceProviderEngineScope.cs:49-53, 283-298`).

---

## ADR-MSG-023: The Inbox Back-off Is Held per Consumer and Tenant

**Status:** Accepted
**Date:** 2026-11-24
**Issue:** #210
**Amends:** #124's inbox back-off (no ADR: its record is the README, *One tenant down*).

### Context

#124 held the inbox back-off per (store or instance, tenant): any consumer's success for tenant A
cleared A's entry, and A's entry deferred every consumer's deliveries for A. With consumer X's dependency
down for A and consumer Y healthy, Y's successes kept clearing A, so X's back-off never grew and X's rows
for A were retried every polling interval against the down dependency; between Y's successes, Y's
deliveries for A waited behind X. What the engine knows when a guarded handler fails is the store
unreachable or not, an `InboxDependencyUnavailableException` with no scope, and the row's tenant or none:
nothing tells a tenant's database from the consumer's own dependency.

### Decision

1. **The unit.** The inbox back-off is held per (store or instance, consumer, tenant). The consumer is the
   row's `ConsumerType` (`MessageConsumerKey`), so a handler class consuming two message types has one
   back-off. A new delivery, a retry, the retry order and the claim read the pair; a success of (C, T)
   clears, of the pairs, (C, T) only — and C's own probe and back-off, never another consumer's. The state is one internal `InboxBackoff` per store or instance — entries tenant →
   consumer → (until, delay), and per consumer its pending probe and its consumer-wide back-off — under one
   lock; `TenantBackoff` stays the outbox's, unchanged.
2. **Two probes, each deciding one thing.**
   - The **store probe** decides the store-wide back-off, on its evidence (PR #219, A25 — replacing the
     withdrawal of a held consumer's probe and its exception, A19 and A22). The evidence is reset on any success, on `Shared`, on
     `MarkSuspected` and when no entry is left. The **counted tenants**: a down verdict (not `Held`) counts
     its tenant when that tenant had no entry for any consumer before it, or while the store's inbox is
     suspected — one per verdict that counts, as #124's probe counts verdicts (read as a set, the same
     tenant counted twice while suspected would differ from #124: V2 failed on 43 of 100 seeds). The
     **failed consumers**: every consumer with a down verdict since the reset, with the tenants it failed
     for. On a down verdict of C for T, after `Held` and the pair's entry: when the counted tenants number
     two or more, `Shared` — the transport pauses, the retry batch is abandoned, the store is suspected,
     every consumer's pending probe clears, no consumer back-off is set — unless the only failed consumer
     is C and C has a consumer-wide back-off on record (in force, or ended and neither pruned nor cleared
     by C's own success): then `Consumer`, C held again on its curve. Otherwise C's consumer probe decides
     between `Consumer` and `Tenant`. A `Consumer` verdict withdraws C from the evidence: the tenants only
     C failed for leave the counted tenants, and C leaves the failed consumers. A success of any consumer
     clears the evidence and the suspicion. With one consumer nothing is on record: this is #124's probe.
   - The **consumer probe** decides a consumer-wide back-off, which never pauses anything: a verdict of C
     for a tenant that had no entry for C, with C's probe pending from another tenant, puts C in back-off
     for every tenant (`Consumer`), on the inbox curve, growing from its previous one; otherwise C's probe
     is pending. A verdict for a tenant that already had an entry for C changes that entry only, so two
     tenants that never recover hold C at most once: known to C, they cannot hold it again. Only a success
     of C clears C's probe and back-off;
     C's pending probe also clears once C has no entry left.
   - **A store-wide verdict wins.** A `Shared` verdict sets no consumer back-off, and entering the
     store-wide back-off (`Shared`, or `MarkSuspected` for the store unreachable or a verdict with no tenant)
     clears every consumer's pending probe: C's second failure puts it in consumer-wide back-off only when
     it is not the store probe failing — another consumer succeeded since, or its tenant already failed for
     another consumer. With one consumer it never occurs, and the state answers exactly as `TenantBackoff`
     — verified on generated sequences.
   - **Why this evidence.** A store-wide outage shows as different consumers failing for different tenants
     with no success between (V7), however their records stand: two consumers on record failing for the
     same tenants trip it too — X for T1, W for T1, X for T2. A consumer's own dependency shows as that
     consumer failing alone: the withdrawal keeps a single later failure of another consumer from pausing
     the store (V17), and the exemption keeps a held consumer's backlog, filling a retry claim alone — the
     store suspected or not — from abandoning the batch on its second row, batch after batch (V16). A19
     and A22, two patches on one flag, each opened a new stop-rule case; this statement replaces them.
   - **Two limits remain.** A batch holding only one consumer's rows, before that consumer has any
     consumer-wide back-off on record, still reads two of its failures, for two tenants with no success
     between, as the store probe failing, and abandons the batch, as on main: nothing in the verdict tells
     that from a store-wide outage. And two failures in a row of one consumer on record, with no other
     consumer failing between, are read as that consumer's own, even in a store-wide outage: X, X then W,
     W holds X and W each for every tenant and pauses nothing. That is the sequence when each retry claim
     is filled by one held consumer's backlog in turn, and on the delivery path when one consumer's
     deliveries fail in a row.
   - **Held.** A verdict while the pair or its consumer is in back-off changes nothing.
3. **The claim key.** `IInboxProcessorStore.ClaimBatchAsync` takes `InboxClaimExclusions` — the consumers
   and the (consumer, tenant) pairs in back-off — in place of the excluded tenants: one signature, no
   default implementation. A row with no tenant is never left out. The EF store sends one array parameter
   of consumers and one of keys `ConsumerType + U+001F + TenantId`, compared with the same expression on the
   columns; one SQL text for any exclusion, main's for none. The match is exact because no consumer key
   contains U+001F: `MessageConsumerKey.Of` and the `LocalConsumer` constructor refuse one, so the first
   separator of a key is at its consumer's length; a tenant id may contain anything, as the suffix.
4. **Why no structured pair.** EF Core 10 translates parameter collections of primitive values only
   (`RelationalQueryableMethodTranslatingExpressionVisitor.TranslatePrimitiveCollection`); a collection of
   complex types maps only to a JSON column (EF Core, *Complex types*); and `unnest` would be PostgreSQL SQL
   in the provider-neutral package.
5. **Retry order.** A batch tries the rows of pairs with no entry first, in claim order, then the others by
   their pair's delay. At each consumer's first dependency verdict in the batch that holds its pair or
   itself (a verdict that abandons the batch reorders nothing), the rows not yet attempted are ordered
   again, round-robin across consumers, each consumer's rows keeping their order — the order of pairs then
   holds within each consumer's rows only; each round tries first the consumers with no such verdict in the
   batch and no consumer-wide back-off on record (in force, or ended and neither pruned nor cleared by a
   success), then those with one on record, then those that met such a verdict in the batch, in the order
   they met it. Computed at most once per consumer per batch, so a batch with no failure keeps claim order
   and pays nothing for it, and with one consumer left, or none, the order is unchanged. When no consumer succeeds in between, the store probe still trips. Without it, a consumer's held backlog —
   the oldest rows once its consumer-wide back-off ends — heads the claim, and two of its rows failing back
   to back, before any success, fail the store probe: the batch is abandoned before the other consumers'
   rows, batch after batch, with no transport traffic to bring a success in between.
6. **Observability.** `microkit.inbox.tenants.backoff` keeps its name, unit and meaning: a tenant counts
   while at least one of its pairs is unrecovered. `microkit.inbox.consumers.backoff` (`{consumer}`, the
   same tags and rule) counts the consumers in consumer-wide back-off: during one, a consumer's pairs age
   out with no attempt, and the tenants counter alone would read 0 while the consumer is still down. Event
   2013 names the consumer; 2015 (`Warning`) reports a consumer entering consumer-wide back-off, at most
   once per entry — after the row or the batch that caused it is written, so not when that write fails;
   2014 keeps its id, level and parameters.

### Consequences

- **Costs, documented.** When a tenant's database is down, each consumer of the tenant makes one uncounted
  attempt before it waits: the handler cannot say the cause is its tenant's. "At most two attempts per
  batch" holds between two successes: while another consumer keeps succeeding, a consumer-wide outage
  costs at most two attempts on tenants new to that consumer per consumer back-off, plus each known pair
  on its own curve. Pairs and consumers are compared by the columns' collation, as tenant ids were.
- **The bound, measured.** Each claim reads, and never writes, the claimable rows of a held pair or
  consumer. Against #124's tenant claim at equal exclusion, on PostgreSQL with 20,000 and 2,000 held rows:
  the same plan, the same shared buffers (976, 98) and the same rows read past, at equal exclusion; plus the key comparison per
  row read — Npgsql's translation evaluates it twice, EF Core's null compensation — about 40% more
  execution time at 20,000 rows (6.0–6.8 ms against 8.3–9.7 ms, medians of two runs), within noise at
  2,000. With 100 pairs the planner takes a bitmap scan and reads 13% more buffers. Paid once per batch,
  and only while something is held (`TenantExclusionPlanTests`, D11).
- **Breaking.** The claim SPI's signature changes; a store provider implements the new filter.
  `MessageConsumerKey.Of` and `LocalConsumer` refuse a key containing U+001F, which no C# type name
  contains: a key gets one only from an assembly name or a consumer source's own name. A row written by hand with such a `ConsumerType` names no consumer and can only be left out of
  the claim while a pair whose key equals its own is held.
- **Unchanged.** Rows and deliveries with no tenant; the store unreachable pausing or abandoning for every
  tenant (a separate finding); local deliveries; the outbox back-off; `DeadLetterPendingAsync(tenantId)`,
  which covers every consumer of the tenant; a dependency verdict never counts an attempt.
- **Prior art.** MassTransit `d9f61f7`: `UseCircuitBreaker` is a pipe filter that can be put on one
  consumer's pipe (`CircuitBreakerConfigurationExtensions.cs:16`) and fails the call without reaching the
  consumer while open (`OpenBehavior.cs:33-35`); its kill switch stops a whole receive endpoint
  (`KillSwitchConfigurationExtensions.cs:11-15`). Wolverine `3ee64e3`: the circuit breaker pauses its
  listener (`CircuitBreaker.cs:192-204`). NServiceBus `1f68338`: consecutive failures rate-limit the whole
  endpoint (`RateLimitSettings.cs:9-16`). CAP `a4f8cd3`: no breaker, each received message retried on its
  own (`IProcessor.NeedRetry.cs:90-99`). None holds a tenant. The consumer back-off here is MassTransit's
  per-consumer breaker; the store-wide back-off is the others' endpoint pause.
