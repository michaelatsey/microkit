# Rule: Logging Architecture

These rules govern the structural integrity of MicroKit.Logging. They are enforced by the `logging-architect` agent, `logging-dependency-guardian` agent, and `MicroKit.Logging.ArchitectureTests`.

## Module Boundaries

### Layer 0 — Abstractions (MicroKit.Logging.Abstractions)
- Contains: interfaces, constants, records (contracts), enums
- Allowed dependencies: `Microsoft.Extensions.Logging.Abstractions` only
- Forbidden: any `ProjectReference`, any other NuGet package
- Stability: **STABLE** — breaking changes require major version bump + ADR

### Layer 1 — Core (MicroKit.Logging)
- Contains: enrichment pipeline, context propagation, scope management, DI registration
- Allowed dependencies: `MicroKit.Logging.Abstractions`, `MEL.Abstractions`, `MEL.DI.Abstractions`, `System.Diagnostics.DiagnosticSource`
- Forbidden: OpenTelemetry packages, Serilog packages, AspNetCore packages

### Layer 2 — Providers
Projects: `MicroKit.Logging.OpenTelemetry`, `MicroKit.Logging.Serilog`, `MicroKit.Logging.AspNetCore`, `MicroKit.Logging.Diagnostics`
- Allowed dependencies: `MicroKit.Logging.Abstractions`, `MicroKit.Logging` core, their specific SDK
- Forbidden: cross-provider references (Serilog must not reference OpenTelemetry, etc.)

### Layer 3 — Tooling (build-time only)
Projects: `MicroKit.Logging.Analyzers`, `MicroKit.Logging.Generators`
- These projects are NOT referenced at runtime — they are analyzer/generator packages
- Must not be referenced by any Layer 0/1/2 project

## Cross-Module Rule (Ecosystem-Wide)

> Other MicroKit modules (`MicroKit.MediatR`, `MicroKit.Persistence`, etc.) may ONLY depend on `MicroKit.Logging.Abstractions`. Never on `MicroKit.Logging` core or any provider.

## Abstraction Placement

An interface, record, or constant belongs in `Abstractions` if:
- It is consumed by code outside this module
- It is part of the public contract for enrichment, context, or correlation

It belongs in `Core` if:
- It is an internal implementation detail
- It coordinates between Abstractions contracts
- It would never be referenced by another MicroKit module directly

## Design Invariants

1. `ILogEnricher` is in Abstractions — any module can provide enrichers
2. `IOperationContext` is in Abstractions — any module can read the context
3. `ILogContextAccessor` is in Abstractions — any module can access the ambient context
4. `LogPropertyNames` constants are in Abstractions — canonical names are the shared contract
5. The enrichment pipeline orchestrator is in Core — it is an implementation detail
