---
name: build
description: How to build MicroKit.Persistence reliably. Use whenever you need to compile the module, troubleshoot a build error (CS1591, CPM violation, MKP analyzer diagnostic, trimming warning), restore packages, or understand the build order of the 8 src projects, the tests and the samples.
---

# Skill: Build

## Commands

```bash
# Module solution — always Release: TreatWarningsAsErrors applies to Release only
dotnet build modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release

# One project
dotnet build modules/MicroKit.Persistence/src/MicroKit.Persistence.EntityFrameworkCore/ -c Release

# Samples (README code, built with the analyzers — fails on any MKP diagnostic)
dotnet build modules/MicroKit.Persistence/samples/MicroKit.Persistence.Samples/ -c Release

# Restore only / detailed output
dotnet restore modules/MicroKit.Persistence/MicroKit.Persistence.slnx
dotnet build modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release -v d
```

A default build resolves `MicroKit.Domain` from source; `-p:CIReleaseBuild=true` resolves it from
the published package instead (compatibility check, used by the release workflow).

## Build order

1. `MicroKit.Domain` (cross-module, from source)
2. `MicroKit.Persistence.Abstractions`
3. `MicroKit.Persistence` (core)
4. `EntityFrameworkCore`, `Specifications`, `Testing`
5. `EntityFrameworkCore.PostgreSql`, `EntityFrameworkCore.SqlServer`
6. `MicroKit.Persistence.Analyzers` — standalone, `netstandard2.0`

MSBuild derives the order from the project references.

## Common failures

| Error | Cause | Fix |
|-------|-------|-----|
| `CS1591` in `src/` | Missing XML doc on a public member | Add the `<summary>` (and tags) |
| `NU1008` | `Version=` on a `PackageReference` | Move the version to the root `Directory.Packages.props` |
| `MKP001` / `MKP002` | Read repository commits or mutates | Remove the call or the method |
| `MKP003` | `SaveChanges(Async)` outside `IUnitOfWork` | Commit through `IUnitOfWork` |
| `MKP004` | `DbContext` injected outside infrastructure | Inject a typed repository |
| `MKP005` | Repository method returns `IQueryable<T>` | Return `IReadOnlyList<T>` / `IPagedResult<T>` |
| `IL2091`, `IL2026` | Trimming analysis over EF Core generics | Already suppressed per project; do not add new suppressions without a justification |
| EF Core type not found | Wrong layer | EF Core stays in EntityFrameworkCore and the providers |

## Environment

Root `global.json` pins the SDK. Root `Directory.Build.props`: `net10.0`, `Nullable` and
`ImplicitUsings` enabled, `LangVersion latest`, `AnalysisLevel latest-recommended`,
`EnforceCodeStyleInBuild`, `GenerateDocumentationFile`, `IsTrimmable`, and
`TreatWarningsAsErrors` in Release.
