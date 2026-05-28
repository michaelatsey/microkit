# Rule: Dependencies — MicroKit.MediatR

Rules governing NuGet packages and project references within MicroKit.MediatR.

## Central Package Management (CPM)

All NuGet package versions are declared in `build/Directory.Packages.props` at the monorepo root.

```xml
<!-- ✅ Correct — in Directory.Packages.props -->
<PackageVersion Include="MediatR" Version="12.4.1" />

<!-- ✅ Correct — in .csproj -->
<PackageReference Include="MediatR" />

<!-- ❌ Wrong — version in .csproj -->
<PackageReference Include="MediatR" Version="12.4.1" />
```

Any `Version=` attribute on a `PackageReference` in a `.csproj` is a CPM violation and is blocked by the `dependency-check` hook.

## The 4-Project Layer Graph

```
MicroKit.MediatR.Abstractions   ← contrats CQRS, markers, DomainEvent contracts
        ↑
MicroKit.MediatR (core)         ← DI, dispatch, BehaviorBase, PipelineOrder, IDomainEventDispatcher
        ↑
MicroKit.MediatR.Behaviors      ← 6 behaviors out-of-the-box
MicroKit.MediatR.Testing        ← harnesses (dépend du core, pas des Behaviors)
```

## Allowed Package Matrix

| Project | Allowed Packages |
|---------|-----------------|
| `Abstractions` | `MediatR.Contracts`, `MicroKit.Domain.Abstractions`, `MicroKit.Logging.Abstractions`, `MicroKit.Result` |
| `MicroKit.MediatR` (core) | Abstractions (project) + `MediatR`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `Behaviors` | Abstractions + Core (projects) + `FluentValidation`, `Polly`, `MicroKit.Logging.Abstractions` |
| `Testing` | Abstractions + Core (projects) + `NSubstitute` |

## Project Reference Rules

```
Abstractions   →  (no project references — package refs only)
Core           →  Abstractions
Behaviors      →  Abstractions, Core
Testing        →  Abstractions, Core
```

- `Behaviors` and `Testing` are **siblings** — `Testing` must not reference `Behaviors` and vice versa.
- `FluentValidation` and `Polly` are confined to `Behaviors`. They must never appear in Abstractions, Core, or Testing.
- `NSubstitute` is confined to `Testing`. It must never appear in a shipped runtime package.

## Cross-Module Dependencies (Ecosystem)

MicroKit.MediatR is a **Level 2** module. It may depend on Level 0 modules:

```
MicroKit.MediatR.Abstractions → MicroKit.Result (production dependency — see ADR-001)
MicroKit.MediatR.Abstractions → MicroKit.Domain.Abstractions (domain event contracts)
MicroKit.MediatR.Abstractions → MicroKit.Logging.Abstractions (LogPropertyNames only)
```

Forbidden:
- A dependency on any module **higher** in the graph (Persistence, Messaging, Http, Multitenancy)
- A dependency on a concrete (non-Abstractions) package of another module — except `MicroKit.Result`, which ships as a single package and is allowed per ADR-001
- A `ProjectReference` crossing module boundaries — use `PackageReference` (see root `.claude/rules/module-boundaries.md`)

## The MicroKit.Result Dependency (ADR-001)

Unlike MicroKit.Logging (which forbids a Result dependency), MicroKit.MediatR **deliberately depends on `MicroKit.Result`** in its Abstractions:

- Handlers commonly return `Result<T>`; the contracts must be able to express `ICommand<Result<T>>`
- The behaviors (Validation, Authorization) produce `Result.Failure(...)` when `TResponse` is a `Result<T>`
- This is a justified, permanent dependency. See `.claude-context/context/architectural-decisions.md` ADR-001.

## Adding a New Dependency

Before adding any new `PackageReference`:
1. Check it is not already available transitively
2. Add it to `Directory.Packages.props` first
3. Verify the package is compatible with .NET 10
4. Confirm it lands in the correct layer (e.g., a new resilience package belongs in `Behaviors`, not core)
5. If adding to `Abstractions`: requires explicit `api-reviewer` approval — open a discussion first
