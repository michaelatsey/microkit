# Context: Ecosystem Overview

**Position of MicroKit.Logging within the MicroKit ecosystem.**

---

## Role

MicroKit.Logging is the **observability platform** of the MicroKit ecosystem. It is not a logging framework — it is the standardization and correlation layer that makes all other modules observable in a coherent, queryable way.

Every MicroKit module that performs logging or tracing does so through the contracts defined in `MicroKit.Logging.Abstractions`.

## Ecosystem Position

```
Consumer Application
        │
        ▼
┌───────────────────────────────────────────────┐
│              MicroKit Modules                  │
│  ┌──────────┐ ┌───────────┐ ┌──────────────┐  │
│  │ MediatR  │ │Persistence│ │MultiTenancy  │  │
│  └────┬─────┘ └─────┬─────┘ └──────┬───────┘  │
│       │             │              │           │
│       └─────────────┼──────────────┘           │
│                     │                          │
│                     ▼                          │
│        MicroKit.Logging.Abstractions           │
│        (ILogEnricher, IOperationContext,       │
│         LogPropertyNames, ...)                 │
│                     │                          │
│                     ▼                          │
│           MicroKit.Logging (Core)              │
│         Enrichment Pipeline + Context          │
│                     │                          │
│          ┌──────────┼──────────┐               │
│          ▼          ▼          ▼               │
│       OTEL        Serilog   AspNetCore         │
└───────────────────────────────────────────────┘
        │            │            │
        ▼            ▼            ▼
   OTEL Collector   Seq        HTTP logs
   Jaeger/Tempo    Elastic    App Insights
```

## What MicroKit.Logging Provides to Other Modules

| Module | What it gets from MicroKit.Logging |
|--------|----------------------------------|
| `MicroKit.MediatR` | Provides `CommandName`, `QueryName` via `ILogEnricher` |
| `MicroKit.Persistence` | Provides `TransactionId` via `ILogEnricher` |
| `MicroKit.MultiTenancy` | Provides `TenantId` via `ILogEnricher` |
| `MicroKit.Auth` | Provides `UserId` via `ILogEnricher` |
| `MicroKit.Messaging` | Provides `MessageId`, `RetryCount` via `ILogEnricher` |
| All modules | Use `LogPropertyNames` constants for consistent property names |

## What Consumers (Applications) Get

- Correlated logs across all MicroKit modules with a single `CorrelationId`
- W3C-compatible distributed tracing via `Activity` + OTEL bridge
- Enriched structured logs queryable in Seq, Elastic, Grafana, Datadog, Azure Monitor
- Zero configuration required — enrichers activate automatically via DI

## Versioning Context

MicroKit.Logging is a **Level 1 module** in the dependency graph:

```
Level 0: Domain · Result
Level 1: Logging → Result (optional) | Caching → Result | Auth → Result+Domain
Level 2: Observability → Result+Logging | Persistence → Result+Domain
...
```

`MicroKit.Logging.Abstractions` has no dependency on `MicroKit.Result`. This is intentional — the Abstractions package must be adoptable by any .NET 10 project with no MicroKit prerequisites.
