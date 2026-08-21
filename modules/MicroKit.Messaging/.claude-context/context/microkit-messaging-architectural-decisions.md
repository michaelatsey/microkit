# Context: Architectural Decisions — MicroKit.Messaging

**ADR (Architecture Decision Records) for MicroKit.Messaging and MicroKit.Messaging.MediatR.**

Format: `## ADR-MSG-{NNN}: {Title}` · Status: `Accepted` | `Proposed` | `Superseded` | `Deprecated`

> ADR-MSG-001 through ADR-MSG-009 are documented in `.claude/rules/microkit-messaging-architecture.md`.
> This file contains ADRs created during the `fix/messaging/mediatr` branch.

---

## ADR-MSG-010: DomainEvent vs DomainEventNotification — Two Disjoint Types, Four Dispatch Phases

**Status:** Accepted  
**Date:** 2026-06-22  

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
