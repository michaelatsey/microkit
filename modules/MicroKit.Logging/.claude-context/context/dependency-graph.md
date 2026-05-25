# Context: Dependency Graph

**Current state of all project dependencies within MicroKit.Logging.**

Updated whenever a `<ProjectReference>` or significant `<PackageReference>` is added. Agent `dependency-guardian` validates against this graph automatically.

---

## Project Reference Graph

```
MicroKit.Logging.Abstractions
│   └── [no project references]
│   └── NuGet: Microsoft.Extensions.Logging.Abstractions
│
MicroKit.Logging (Core)
│   ├── → MicroKit.Logging.Abstractions
│   └── NuGet: MEL.Abstractions, MEL.DI.Abstractions, System.Diagnostics.DiagnosticSource
│
MicroKit.Logging.Diagnostics
│   ├── → MicroKit.Logging.Abstractions
│   ├── → MicroKit.Logging
│   └── NuGet: System.Diagnostics.DiagnosticSource
│
MicroKit.Logging.AspNetCore
│   ├── → MicroKit.Logging.Abstractions
│   ├── → MicroKit.Logging
│   └── NuGet: Microsoft.AspNetCore.Http.Abstractions
│
MicroKit.Logging.OpenTelemetry
│   ├── → MicroKit.Logging.Abstractions
│   ├── → MicroKit.Logging
│   └── NuGet: OpenTelemetry, OpenTelemetry.Extensions.Hosting
│
MicroKit.Logging.Serilog
│   ├── → MicroKit.Logging.Abstractions
│   ├── → MicroKit.Logging
│   └── NuGet: Serilog, Serilog.Extensions.Hosting
│
MicroKit.Logging.Analyzers
│   └── [build-time only, no project references]
│   └── NuGet: Microsoft.CodeAnalysis.CSharp
│
MicroKit.Logging.Generators
│   └── [build-time only, no project references]
│   └── NuGet: Microsoft.CodeAnalysis.CSharp
```

## Cross-Module Dependencies (Ecosystem)

```
MicroKit.MediatR          → MicroKit.Logging.Abstractions (only)
MicroKit.Persistence      → MicroKit.Logging.Abstractions (only)
MicroKit.MultiTenancy     → MicroKit.Logging.Abstractions (only)
MicroKit.Auth             → MicroKit.Logging.Abstractions (only)
MicroKit.Messaging        → MicroKit.Logging.Abstractions (only)
```

**Forbidden:** any of the above depending on `MicroKit.Logging` core or any provider.

## MicroKit.Result — Explicit Non-Dependency

> **ADR-006 (2026-05-25):** `MicroKit.Logging` does **not** depend on `MicroKit.Result`.

The monorepo-level graph lists "Logging → Result (optional)" as theoretically allowed. This is superseded:

- `Abstractions` — zero external deps by design (ADR-001)
- `Core` — enrichers return `void`, errors are swallowed internally, `Result<T>` adds no value
- Circular dependency risk: if `MicroKit.Result` ever uses `ILogger`, a reverse dependency would create a cycle

This is a **permanent decision**, not a v1 deferral. The `dependency-guardian` agent enforces it.

## NuGet Package Versions

> Canonical versions are in `Directory.Packages.props`. This section documents the intent.

| Package | Used By | Notes |
|---------|---------|-------|
| `Microsoft.Extensions.Logging.Abstractions` | All projects | MEL stable |
| `System.Diagnostics.DiagnosticSource` | Core, Diagnostics | Includes Activity |
| `OpenTelemetry` | OpenTelemetry project only | Opt-in |
| `Serilog` | Serilog project only | Opt-in |
| `Microsoft.AspNetCore.Http.Abstractions` | AspNetCore project only | Opt-in |
| `Microsoft.CodeAnalysis.CSharp` | Analyzers, Generators | Build-time only |
