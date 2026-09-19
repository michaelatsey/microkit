---
paths:
  - "modules/MicroKit.Domain/**"
  - "modules/MicroKit.MediatR/**"
  - "modules/MicroKit.Messaging/**"
---

# Domain events

## Event taxonomy (canonical)

```txt
MicroKit.Domain.Events.IEvent          ← canonical root (Domain module)
  IDomainEvent : IEvent                ← domain events (Domain module)
  IIntegrationEvent : IEvent           ← integration events (Messaging module)

MicroKit.MediatR.Events.IEvent         ← [Obsolete] shim → use MicroKit.Domain.Events.IEvent
```

## Domain event dispatch topology (ADR-MEDIATR-009)

```txt
Domain Event  (accumulated on the tracked aggregate)
    │
    ▼ P1  IDomainEventsProvider.DrainDomainEvents()   collect · one pass · not recursive
    │
    ├──► P2 IDomainEventHandler<TEvent>         sync · in-transaction · DI direct · raw event
    │        (bypasses MediatR pipeline behaviors intentionally)
    │
    └──► P3 DomainEventNotification<TEvent>     built via IDomainEventNotificationFactory
                 │                                (null when the event has no mapping)
                 ▼ P4 IOutboxWriter.AddBatchAsync   staged in the SAME transaction
                 │
                 ▼ (outbox processor · at-least-once · after commit)
          INotificationHandler<TNotification>   async · idempotent · technical/integration
```

**Composition — ADR-MEDIATR-014.** One `IDomainEventsDispatcher` implementation
orchestrates the whole sequence. Further in-transaction participants contribute through an
ordered, possibly empty `IEnumerable<IDomainEventsSink>` resolved from DI: MicroKit.MediatR
registers zero sinks, MicroKit.Messaging.MediatR contributes the outbox sink via
`AddMediatRDomainEvents()`. Order-independent by construction — supersedes the
`TryAdd`/`Replace` precedence contract of ADR-MEDIATR-013. **PR #84's core-side `TryAdd`
stays correct and must not be reverted** — it still protects a consumer's own dispatcher.

**Loud failure — ADR-MEDIATR-015.** A `DomainEventNotification<TEvent>` discovered by the
scan with **no** `IDomainEventsSink` registered throws on the first dispatch of a mapped
event, naming the event, the notification and the missing registration. Zero sinks with no
notifications stays valid and free. Register a sink with `TryAddEnumerable` and an
implementation type or instance; a factory lambda is rejected.

> The Messaging outbox model has been rebuilt since ADR-MEDIATR-015 (one reentrant table,
> routing by `MessageKind`, `.MediatR` decorates the standard dispatcher). See ADR-MSG-019.
