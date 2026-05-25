# Skill: Build

How to build MicroKit.Logging reliably in all configurations.

## Commands

```bash
# Full solution build (Debug)
dotnet build modules/MicroKit.Logging/MicroKit.Logging.slnx -c Debug

# Full solution build (Release — enables TreatWarningsAsErrors)
dotnet build modules/MicroKit.Logging/MicroKit.Logging.slnx -c Release

# Single project build (faster iteration)
dotnet build modules/MicroKit.Logging/src/MicroKit.Logging.Abstractions/ -c Debug

# Build with detailed output (troubleshooting)
dotnet build modules/MicroKit.Logging/MicroKit.Logging.slnx -c Debug -v d

# Restore only
dotnet restore modules/MicroKit.Logging/MicroKit.Logging.slnx
```

## Build Order (dependency-safe)

When building incrementally, respect this order:
1. `MicroKit.Logging.Abstractions`
2. `MicroKit.Logging`
3. `MicroKit.Logging.Diagnostics`
4. `MicroKit.Logging.OpenTelemetry`, `MicroKit.Logging.Serilog`, `MicroKit.Logging.AspNetCore` (parallel)
5. `MicroKit.Logging.Analyzers`, `MicroKit.Logging.Generators` (independent, build-time only)

The `.slnx` solution file respects this order automatically.

## Common Build Failures

| Error | Cause | Fix |
|-------|-------|-----|
| `CS1591` in src/ | Missing XML doc on public member | Add `<summary>` tag |
| `PackageReference` with `Version=` | CPM violation | Move version to `Directory.Packages.props` |
| `NETSDK1004` | Missing global.json SDK version | Check `global.json` at monorepo root |
| Analyzer compilation error | Roslyn version mismatch | Verify `Microsoft.CodeAnalysis.CSharp` version in `Directory.Packages.props` |

## Build Environment

- **SDK**: version locked in `global.json` at monorepo root
- **Target framework**: `net10.0` for all `src/` projects
- **Nullable**: `enable` (enforced via `Directory.Build.props`)
- **ImplicitUsings**: `enable`
- **LangVersion**: `latest`
- **TreatWarningsAsErrors**: `true` in Release configuration only
