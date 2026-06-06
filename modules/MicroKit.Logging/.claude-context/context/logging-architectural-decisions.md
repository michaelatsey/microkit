# Context: Logging Architectural Decisions

**ADR (Architecture Decision Records) for MicroKit.Logging.**

Format: `## ADR-{NNN}: {Title}` · Status: `Accepted` | `Proposed` | `Superseded` | `Deprecated`

---

## ADR-001: Abstractions as the Only Cross-Module Contract

**Status:** Accepted  
**Date:** 2026-05-24  

### Decision

All MicroKit modules that want to integrate with MicroKit.Logging must depend only on `MicroKit.Logging.Abstractions`, never on `MicroKit.Logging` core or any provider package.

### Rationale

- Prevents a "logging framework tax" — a module requiring Serilog or OTEL just to compile
- Allows the full logging pipeline to be swapped without touching consuming modules
- Keeps the dependency graph acyclic and shallow (Abstractions has one optional dependency)

### Consequences

- `ILogEnricher`, `IOperationContext`, `LogPropertyNames` must live in Abstractions
- `ILogScopeFactory` and `IAsyncLogScopeFactory` both live in Abstractions — modules needing async scope creation do not require a Core dependency
- Abstractions must remain extremely stable — changes are ecosystem-wide breaking changes
- The enrichment pipeline orchestrator lives in Core, invisible to consumers

### Amendment — 2026-05-25

`IAsyncLogScopeFactory` was moved from `MicroKit.Logging` core to `MicroKit.Logging.Abstractions` to maintain symmetry with `ILogScopeFactory` and to allow modules that register async enrichers to create scopes without depending on Core. The interface has zero implementation dependencies (only BCL types and `OperationScopeOptions`).

---

## ADR-002: No IMicroKitLogger Wrapper

**Status:** Accepted  
**Date:** 2026-05-24  

### Decision

MicroKit.Logging does not define its own `IMicroKitLogger` interface. It extends `ILogger<T>` from `Microsoft.Extensions.Logging` via enrichment and extension methods.

### Rationale

- Avoids a parallel logging abstraction that every team must adopt or bridge
- `ILogger<T>` is already the de-facto .NET standard
- Wrapping adds cost (abstraction layer, mock complexity) with no benefit
- Enrichers and scopes solve the value-add problem without a new interface

### Consequences

- All value is delivered via enrichers, scopes, and `LoggerMessage` patterns
- No migration required for existing `ILogger<T>` consumers

---

## ADR-003: OpenTelemetry as an Opt-In Bridge

**Status:** Accepted  
**Date:** 2026-05-24  

### Decision

OTEL integration is confined to `MicroKit.Logging.OpenTelemetry`. No other project in the module may reference OTEL packages.

### Rationale

- OTEL SDK is heavy; not every MicroKit consumer needs it
- Correlation (TraceId, SpanId via Activity) works without OTEL — `Activity` is in `System.Diagnostics`
- Allows consumers to use NLog + OTEL or Serilog + OTEL without MicroKit forcing the combination

### Consequences

- Basic correlation works without `MicroKit.Logging.OpenTelemetry`
- Full OTEL Logs + Traces export requires opting in to `MicroKit.Logging.OpenTelemetry`

---

## ADR-004: Performance-First Enrichment Pipeline

**Status:** Accepted  
**Date:** 2026-05-24  

### Decision

The enrichment pipeline always checks `ILogger.IsEnabled(level)` before executing any enricher. Enrichers that compute nothing on a disabled log level must allocate zero bytes.

### Rationale

- MicroKit.Logging is on every log call — a 100ns overhead per call becomes 100ms/second at 1M logs/s
- Library code must be invisible from a performance perspective
- Source generators (`[LoggerMessage]`) eliminate boxing on hot paths

### Consequences

- Enrichers must implement a fast no-op path when `IsEnabled` returns false
- The pipeline short-circuits before calling any enricher
- Performance budget is tracked in `.claude-context/standards/logging-performance-budget.md`

---

## ADR-005: Canonical Property Names as Shared Contract

**Status:** Accepted  
**Date:** 2026-05-24  

### Decision

All property names used in log scopes, enrichers, Activity tags, and DiagnosticSource payloads are defined as constants in `LogPropertyNames` in `MicroKit.Logging.Abstractions`. No string literals are allowed outside of this class.

### Rationale

- Log backends (Seq, Elastic, Datadog) require consistent field names for indexing and alerting
- Typos in field names produce silently broken queries and missing data
- A Roslyn analyzer (`MKL0022`) enforces this at compile time

### Consequences

- New properties require an update to `LogPropertyNames` + `log-properties.md` + API review
- The Roslyn analyzer family `MKL002x` enforces usage at compile time

---

## ADR-006: MicroKit.Logging Does Not Depend on MicroKit.Result

**Status:** Accepted  
**Date:** 2026-05-25  
**Triggered by:** `architect` agent review — conflict between monorepo graph ("Logging → Result allowed") and module design.

### Decision

`MicroKit.Logging.Abstractions` and `MicroKit.Logging` core do **not** depend on `MicroKit.Result`. This applies to all 8 projects in the module.

### Rationale

1. **Abstractions purity** — `MicroKit.Logging.Abstractions` has zero third-party dependencies by design (ADR-001). `MicroKit.Result` would be its first external dependency, breaking the contract.
2. **Enricher contract is void** — `ILogEnricher.Enrich()` returns `void`. Enrichment errors are swallowed and logged internally, never propagated to callers. No return value to wrap in `Result<T>`.
3. **Circular dependency risk** — if `MicroKit.Result` ever uses `ILogger` for internal diagnostics, it would reference `MicroKit.Logging.Abstractions`. A reverse dependency `MicroKit.Logging → MicroKit.Result` would create a cycle.
4. **Standard exceptions are sufficient** — framework-level failures use `ArgumentNullException`, `InvalidOperationException`. No `Result<T>` needed.

### Consequences

- Pipeline errors are handled internally: caught, logged via `ILogger`, never surfaced as `Result<T>`
- The monorepo graph entry "Logging → Result (optional)" is superseded by this decision for v1 and beyond
- `dependency-guardian` agent and `dependency-check` hook enforce this at every `.csproj` change
