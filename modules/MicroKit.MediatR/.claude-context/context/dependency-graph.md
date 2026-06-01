# Context: Dependency Graph

**Current state of all project dependencies within MicroKit.MediatR.**

Updated whenever a `<ProjectReference>` or significant `<PackageReference>` is added. The
`dependency-guardian` agent and the `dependency-check` hook validate against this graph automatically.

---

## Project Reference Graph (4 projects)

```
MicroKit.MediatR.Abstractions
│   └── [no project references — package refs only]
│   └── NuGet: MediatR.Contracts, MicroKit.Domain.Abstractions,
│              MicroKit.Logging.Abstractions, MicroKit.Result
│
MicroKit.MediatR (core)
│   ├── → MicroKit.MediatR.Abstractions
│   └── NuGet: MediatR, Microsoft.Extensions.DependencyInjection.Abstractions
│
MicroKit.MediatR.Behaviors
│   ├── → MicroKit.MediatR.Abstractions
│   ├── → MicroKit.MediatR
│   └── NuGet: FluentValidation, Polly, MicroKit.Logging.Abstractions
│
MicroKit.MediatR.Testing
│   ├── → MicroKit.MediatR.Abstractions
│   ├── → MicroKit.MediatR
│   └── NuGet: NSubstitute
```

> `Behaviors` and `Testing` are **siblings** — neither references the other (sibling isolation).

## Package Confinement

| Package | Confined to | Forbidden elsewhere because |
|---------|-------------|-----------------------------|
| `MediatR` (engine) | core | Abstractions uses `MediatR.Contracts` only |
| `FluentValidation` | Behaviors | validation is a behavior concern |
| `Polly` | Behaviors | resilience is a behavior concern |
| `NSubstitute` | Testing | test-only; must not ship in a runtime package |
| `FluentAssertions` | **banned everywhere** | commercial license (Xceed EULA) |

## Cross-Module Dependencies (Ecosystem)

MicroKit.MediatR is a **Level 2** module:

```
Level 0: Domain · Result
Level 1: Logging → Result(opt) | Caching → Result | Auth → Result+Domain
Level 2: MediatR → Result+Domain | Persistence → Result+Domain | Observability → Result+Logging
```

Allowed cross-module references (via PackageReference, never ProjectReference):

```
MicroKit.MediatR.Abstractions → MicroKit.Result               (production — ADR-001)
MicroKit.MediatR.Abstractions → MicroKit.Domain.Abstractions  (domain event contracts)
MicroKit.MediatR.Abstractions → MicroKit.Logging.Abstractions (LogPropertyNames bridge only)
MicroKit.MediatR.Behaviors    → MicroKit.Logging.Abstractions (LoggingBehavior properties)
```

**Forbidden:**
- A dependency on any Level 3+ module (Http, Messaging, Multitenancy)
- A concrete (non-Abstractions) package of another module — **except** `MicroKit.Result`, which ships
  as a single package and is allowed per ADR-001
- A `ProjectReference` crossing module boundaries

## MicroKit.Result — Explicit Dependency (contrast with Logging)

Unlike MicroKit.Logging (whose ADR-006 forbids a Result dependency), MicroKit.MediatR **deliberately
depends on `MicroKit.Result`** in Abstractions. The reason: MediatR contracts are result-bearing
(`ICommand<Result<T>>`) and the behaviors construct `Result.Failure(...)`, whereas Logging's enricher
contract returns `void`. See ADR-001.

## NuGet Package Versions

> Canonical versions are in `build/Directory.Packages.props`. This documents intent.

| Package | Used By | Notes |
|---------|---------|-------|
| `MediatR.Contracts` | Abstractions | Marker interfaces only |
| `MediatR` | core | Dispatch engine |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | core | DI registration |
| `MicroKit.Result` | Abstractions | Result-bearing contracts (ADR-001) |
| `MicroKit.Domain.Abstractions` | Abstractions | Domain event contracts |
| `MicroKit.Logging.Abstractions` | Abstractions, Behaviors | `LogPropertyNames` |
| `FluentValidation` | Behaviors only | ValidationBehavior |
| `Polly` | Behaviors only | RetryBehavior |
| `NSubstitute` | Testing only | Test helpers |
