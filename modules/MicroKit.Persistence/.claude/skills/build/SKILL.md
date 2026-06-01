---
name: build
description: How to build MicroKit.Persistence reliably in all configurations. Use whenever you need to compile the module, troubleshoot a build error (CS1591, CPM violation, analyzer warning), restore packages, or understand the dependency-safe build order across the 8 projects.
---

# Skill: Build

How to build MicroKit.Persistence reliably in all configurations.

## Commands

```bash
# Full solution build (Debug)
dotnet build modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Debug

# Full solution build (Release — TreatWarningsAsErrors enabled)
dotnet build modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release

# Single project build (faster iteration)
dotnet build modules/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/ -c Debug

# Restore only
dotnet restore modules/MicroKit.Persistence/MicroKit.Persistence.slnx

# Detailed output (troubleshooting)
dotnet build modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Debug -v d
```

## Build Order (dependency-safe)

1. `MicroKit.Persistence.Abstractions`
2. `MicroKit.Persistence` (core)
3. `MicroKit.Persistence.EntityFrameworkCore`
4. `MicroKit.Persistence.EntityFrameworkCore.PostgreSql` and `MicroKit.Persistence.EntityFrameworkCore.SqlServer` (parallel)
5. `MicroKit.Persistence.Specifications`, `MicroKit.Persistence.Testing` (parallel — both depend on Core)
6. `MicroKit.Persistence.Analyzers` (standalone)

The `.slnx` solution file respects this order automatically.

## Common Build Failures

| Error | Cause | Fix |
|-------|-------|-----|
| `CS1591` in src/ | Missing XML doc on public member | Add `<summary>` tag |
| `PackageReference` with `Version=` | CPM violation | Move to `Directory.Packages.props` |
| `PRDANA001` | DbContext in query handler | Inject typed repository instead |
| `PRDANA002` | SaveChanges in read repo | Remove mutation call |
| `PRDANA003` | Missing AsNoTracking | Add `.AsNoTracking()` |
| EF Core type not found | Wrong project reference | Check layer — EF stays out of Abstractions |
| `NETSDK1004` | Missing global.json SDK | Check `global.json` at monorepo root |

## Build Environment

- **SDK**: version locked in `global.json` at monorepo root
- **Target framework**: `net10.0` for all `src/` projects
- **Nullable**: `enable` (enforced via `Directory.Build.props`)
- **ImplicitUsings**: `enable`
- **LangVersion**: `latest`
- **TreatWarningsAsErrors**: `true` in Release configuration only
