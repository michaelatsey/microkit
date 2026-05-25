# Context: Observability Strategy

**The strategic vision driving MicroKit.Logging's design decisions.**

---

## The Problem We Solve

In a modular .NET ecosystem, each module logs independently. Without a shared platform:

- `MicroKit.MediatR` uses `tenant_id` (snake_case)
- `MicroKit.Persistence` uses `tenantId` (camelCase)
- `MicroKit.Auth` uses `TenantIdentifier` (custom)
- Grafana/Elastic has 3 different fields for the same concept

The result: broken dashboards, missing alerts, impossible cross-service correlation.

## The Solution: Observability Platform

MicroKit.Logging is an **observability platform**, not a logging utility. Its value is:

```
Standardization + Correlation + Automatic Enrichment = Observable Ecosystem
```

## Three Pillars

### 1. Standardization

- `LogPropertyNames` constants define the canonical vocabulary
- `MKL002x` analyzers enforce it at compile time
- No negotiation: `TenantId` everywhere, forever

### 2. Correlation

The correlation chain across a distributed request:

```
HTTP Request → Inbound middleware extracts/generates CorrelationId
     ↓
MicroKit.Logging.AspNetCore sets OperationContext
     ↓
CQRS Command (MicroKit.MediatR) reads OperationContext → adds CommandName
     ↓
Database call (MicroKit.Persistence) reads OperationContext → adds TransactionId
     ↓
Outbox publish (MicroKit.Messaging) reads OperationContext → adds MessageId
     ↓
All logs from all modules carry: CorrelationId + TenantId + UserId + CommandName + ...
```

Single query in Seq: `CorrelationId = "abc-123"` → full trace across all modules.

### 3. Enrichment Without Coupling

Enrichers inject context without the consuming module knowing about MicroKit.Logging internals:

```
MicroKit.MultiTenancy → registers TenantLogEnricher via ILogEnricher
MicroKit.Auth         → registers UserLogEnricher via ILogEnricher
MicroKit.MediatR      → registers CqrsLogEnricher via ILogEnricher
                              ↓
                  MicroKit.Logging pipeline
                  collects all enrichers and applies them
```

The consumer registers once, enrichment is automatic.

## Target Observability Backends

MicroKit.Logging is designed to integrate cleanly with:

| Backend | Via |
|---------|-----|
| **Seq** | Serilog + `MicroKit.Logging.Serilog` |
| **Elastic / ELK** | Serilog or OTEL |
| **Grafana + Loki + Tempo** | `MicroKit.Logging.OpenTelemetry` + OTLP |
| **Datadog** | OTEL or custom provider |
| **Azure Monitor** | OTEL or custom provider |
| **Jaeger** | `MicroKit.Logging.OpenTelemetry` + OTLP |

## v1 Scope

MicroKit.Logging v1 delivers:
- ✅ `ILogEnricher` pipeline
- ✅ `IOperationContext` + `AsyncLocal` context propagation
- ✅ `LogPropertyNames` canonical constants
- ✅ W3C TraceContext correlation (via `Activity`)
- ✅ OTEL bridge (optional)
- ✅ Serilog bridge (optional)
- ✅ AspNetCore middleware (optional)
- ✅ Roslyn analyzers for compile-time enforcement
- ✅ Source generators for `[LoggerMessage]` DX

Metrics (beyond what OTEL provides) are **out of scope** for v1.
