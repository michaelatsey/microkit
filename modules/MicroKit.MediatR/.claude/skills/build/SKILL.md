---
name: build
description: How to build MicroKit.MediatR reliably in all configurations. Use whenever you need to compile the module, troubleshoot a build error (CS1591, CPM violation, analyzer mismatch), restore packages, or understand the dependency-safe build order across the 4 projects (Abstractions, core, Behaviors, Testing).
---

# Skill: Build

How to build MicroKit.MediatR reliably in all configurations.

## Commands

```bash
# Full solution build (Debug)
dotnet build modules/MicroKit.MediatR/MicroKit.MediatR.slnx -c Debug

# Full solution build (Release — enables TreatWarningsAsErrors)
dotnet build modules/MicroKit.MediatR/MicroKit.MediatR.slnx -c Release

# Single project build (faster iteration)
dotnet build modules/MicroKit.MediatR/src/MicroKit.MediatR.Abstractions/ -c Debug

# Build with detailed output (troubleshooting)
dotnet build modules/MicroKit.MediatR/MicroKit.MediatR.slnx -c Debug -v d

# Restore only
dotnet restore modules/MicroKit.MediatR/MicroKit.MediatR.slnx
```

## Build Order (dependency-safe)

When building incrementally, respect this order:
1. `MicroKit.MediatR.Abstractions`
2. `MicroKit.MediatR` (core)
3. `MicroKit.MediatR.Behaviors`, `MicroKit.MediatR.Testing` (parallel — both depend only on core)

The `.slnx` solution file respects this order automatically.

## Common Build Failures

| Error | Cause | Fix |
|-------|-------|-----|
| `CS1591` in src/ | Missing XML doc on public member | Add `<summary>` tag |
| `PackageReference` with `Version=` | CPM violation | Move version to `Directory.Packages.props` |
| FluentValidation/Polly type not found in core | Behavior dep referenced from wrong layer | Move the code to `MicroKit.MediatR.Behaviors` |
| `NETSDK1004` | Missing global.json SDK version | Check `global.json` at monorepo root |
| MediatR type not found in Abstractions | Abstractions referenced the engine | Use `MediatR.Contracts` in Abstractions |

## Build Environment

- **SDK**: version locked in `global.json` at monorepo root
- **Target framework**: `net10.0` for all `src/` projects
- **Nullable**: `enable` (enforced via `Directory.Build.props`)
- **ImplicitUsings**: `enable`
- **LangVersion**: `latest`
- **TreatWarningsAsErrors**: `true` in Release configuration only
