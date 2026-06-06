# Standard: Event IDs

**Canonical registry for all diagnostic identifiers in MicroKit.Logging.**

This file has two sections:
1. **Roslyn Diagnostic IDs** — for `MicroKit.Logging.Analyzers`
2. **EventId Registry** — for `ILogger` `EventId` structured logging

Never assign an ID outside this registry. Use `/new-analyzer` to get an ID auto-assigned.

---

## Roslyn Diagnostic IDs (`MKLxxxx`)

### MKL001x — Structured Logging Usage

| ID | Title | Severity | Status |
|----|-------|----------|--------|
| `MKL0011` | Interpolated string used in log message | Warning | Active |
| `MKL0012` | String concatenation used in log message | Warning | Active |
| `MKL0013` | `ToString()` call in log message argument | Warning | Reserved |
| `MKL0014` | Positional placeholder instead of named placeholder | Info | Reserved |

### MKL002x — Property Naming

| ID | Title | Severity | Status |
|----|-------|----------|--------|
| `MKL0021` | Non-canonical log property name used | Warning | Reserved |
| `MKL0022` | Hardcoded property name string instead of `LogPropertyNames` constant | Warning | Reserved |
| `MKL0023` | Deprecated property name used | Warning | Reserved |

### MKL003x — Security

| ID | Title | Severity | Status |
|----|-------|----------|--------|
| `MKL0031` | Sensitive data identifier used as log property name | Error | Active |
| `MKL0032` | Potential PII in log message template | Warning | Reserved |

### MKL004x — Performance

| ID | Title | Severity | Status |
|----|-------|----------|--------|
| `MKL0041` | Expensive expression in log argument without `IsEnabled` guard | Warning | Active |
| `MKL0042` | `params object[]` boxing in log call — use `LoggerMessage` | Info | Reserved |
| `MKL0043` | Log call inside tight loop without level guard | Warning | Reserved |

### MKL005x — API Usage

| ID | Title | Severity | Status |
|----|-------|----------|--------|
| `MKL0051` | `ILogEnricher` registered without `AddMicroKitLogging()` | Error | Reserved |
| `MKL0052` | Enricher mutates shared state | Warning | Reserved |

> **Status values:** `Reserved` (defined, not yet implemented) · `Active` (implemented and shipped) · `Deprecated` (no longer emitted)

---

## EventId Registry

`EventId` values for `ILogger` calls within MicroKit.Logging internals.

Convention: `EventId(range_start + offset, "EventName")`

### Range: 1000–1099 — Enrichment Pipeline

| EventId | Name | Level | Description |
|---------|------|-------|-------------|
| `1001` | `EnrichmentPipelineStarted` | Debug | Enrichment pipeline execution started |
| `1002` | `EnrichmentPipelineCompleted` | Debug | Enrichment pipeline completed (N enrichers, Xms) |
| `1003` | `EnricherFaulted` | Warning | An enricher threw an exception (swallowed, logged) |
| `1004` | `EnrichmentSkipped` | Debug | Enrichment skipped — log level not active |

### Range: 1100–1199 — Context Propagation

| EventId | Name | Level | Description |
|---------|------|-------|-------------|
| `1101` | `CorrelationIdGenerated` | Debug | New `CorrelationId` generated (no inbound value) |
| `1102` | `CorrelationIdPropagated` | Debug | `CorrelationId` extracted from inbound context |
| `1103` | `OperationScopeOpened` | Debug | New operation scope created |
| `1104` | `OperationScopeClosed` | Debug | Operation scope disposed |

### Range: 1200–1299 — OpenTelemetry Bridge

| EventId | Name | Level | Description |
|---------|------|-------|-------------|
| `1201` | `OtelBridgeAttached` | Information | OTEL bridge registered and active |
| `1202` | `ActivityNotFound` | Debug | Log emitted with no active `Activity` — `TraceId`/`SpanId` not enriched |

### Range: 1300–1399 — Diagnostics / ActivitySource

| EventId | Name | Level | Description |
|---------|------|-------|-------------|
| `1301` | `ActivitySourceCreated` | Debug | New `ActivitySource` instantiated |
| `1302` | `ActivityStarted` | Debug | Activity started |
| `1303` | `ActivityStopped` | Debug | Activity stopped |

---

## DiagnosticSource Event Names

Canonical event names for `DiagnosticSource.Write()` calls.

Convention: `MicroKit.{Module}.{Operation}.{State}`

| Event Name | Payload Shape | Description |
|-----------|--------------|-------------|
| `MicroKit.Logging.Enrichment.Executed` | `{ EnricherCount, OperationId, ElapsedMs }` | Pipeline execution completed |
| `MicroKit.Logging.Enrichment.Faulted` | `{ EnricherType, Exception }` | Enricher threw exception |
| `MicroKit.Logging.Scope.Created` | `{ ScopeName, OperationId }` | Log scope opened |
| `MicroKit.Logging.Scope.Disposed` | `{ ScopeName, OperationId }` | Log scope closed |
| `MicroKit.Logging.Correlation.Resolved` | `{ CorrelationId, Source }` | Correlation ID resolved |
