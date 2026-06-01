# Context: Ecosystem Overview

**Position of MicroKit.MediatR within the MicroKit ecosystem.**

---

## Role

MicroKit.MediatR is the **application-messaging layer** of the MicroKit ecosystem. It gives
applications a strongly-typed CQRS surface (commands, queries, streams, domain events) and a
deterministic, opt-in pipeline of cross-cutting behaviors — built on top of Jimmy Bogard's MediatR,
without replacing it.

It is the seam between the presentation layer (controllers, endpoints, message consumers) and the
domain/persistence layers: requests come in as typed commands/queries, flow through the pipeline,
and reach isolated handlers.

## Ecosystem Position

```
Presentation (API endpoints, message consumers, jobs)
        │  SendCommandAsync / SendQueryAsync / StreamQueryAsync
        ▼
┌──────────────────────────────────────────────────────────┐
│                   MicroKit.MediatR                         │
│   ┌────────────────────────────────────────────────────┐ │
│   │ Pipeline: Logging → Authorization → Validation →    │ │
│   │           Idempotency → Caching → Retry → Handler   │ │
│   └────────────────────────────────────────────────────┘ │
│   Contracts: ICommand / IQuery / IStreamQuery / IEvent     │
└──────────────────────────────────────────────────────────┘
        │                    │                     │
        ▼                    ▼                     ▼
  MicroKit.Result      MicroKit.Domain      MicroKit.Logging
  (Result<T> handlers) (domain events)      (CommandName enrichment)
        │
        ▼
  Domain / Persistence (repositories, aggregates)
```

## What MicroKit.MediatR Consumes from Other Modules

| Module | What MicroKit.MediatR uses |
|--------|----------------------------|
| `MicroKit.Result` | `Result<T>` as the canonical handler return; behaviors build `Result.Failure(...)` |
| `MicroKit.Domain.Abstractions` | Domain event contracts wrapped by `DomainEventNotification<T>` |
| `MicroKit.Logging.Abstractions` | `LogPropertyNames.CommandName` for the `LoggingBehavior` |

## What MicroKit.MediatR Provides to the Ecosystem

| Consumer | What it gets |
|----------|--------------|
| Application services | Typed dispatch of commands/queries/streams, isolated testable handlers |
| `MicroKit.Logging` | A `CommandName` enrichment source (the LoggingBehavior emits it) |
| Persistence / Messaging consumers | A clean place to publish domain events after a write commits |
| Test suites | First-class isolation harnesses via `MicroKit.MediatR.Testing` |

## What Consumers (Applications) Get

- Strict CQRS with compile-time-typed contracts
- A deterministic, opt-in behavior pipeline (validation, auth, caching, idempotency, retry, logging)
- Optional `Result<T>` integration — failure as a modeled value, not just exceptions
- Domain-event dispatch decoupled from the request pipeline
- Handlers testable without a DI container, a database, or a real `IMediator`

## Versioning Context

MicroKit.MediatR is a **Level 2 module**. Its Abstractions depend on `MicroKit.Result`,
`MicroKit.Domain.Abstractions`, and `MicroKit.Logging.Abstractions` — all stable, low-level
contracts. It must never depend on a higher-level module (Http, Messaging, Multitenancy).
