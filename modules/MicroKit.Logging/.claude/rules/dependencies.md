# Rule: Dependencies

Rules governing NuGet packages and project references within MicroKit.Logging.

## Central Package Management (CPM)

All NuGet package versions are declared in `build/Directory.Packages.props` at the monorepo root.

```xml
<!-- ✅ Correct — in Directory.Packages.props -->
<PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />

<!-- ✅ Correct — in .csproj -->
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />

<!-- ❌ Wrong — version in .csproj -->
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
```

Any `Version=` attribute on a `PackageReference` in a `.csproj` is a CPM violation and will be blocked by the `dependency-check` hook.

## Allowed Package Matrix

| Project | Allowed Packages |
|---------|-----------------|
| `Abstractions` | `Microsoft.Extensions.Logging.Abstractions` |
| `Core` | `MEL.Abstractions`, `MEL.DI.Abstractions`, `System.Diagnostics.DiagnosticSource` |
| `OpenTelemetry` | Above + `OpenTelemetry`, `OpenTelemetry.Logs`, `OpenTelemetry.Trace` |
| `Serilog` | Above (core) + `Serilog`, `Serilog.Extensions.Hosting` |
| `AspNetCore` | Above (core) + `Microsoft.AspNetCore.Http.Abstractions` |
| `Diagnostics` | Above (core) + `System.Diagnostics.DiagnosticSource` |
| `Analyzers` | `Microsoft.CodeAnalysis.CSharp` |
| `Generators` | `Microsoft.CodeAnalysis.CSharp` |

## Project Reference Rules

```
Abstractions   →  (nothing)
Core           →  Abstractions
OpenTelemetry  →  Abstractions, Core
Serilog        →  Abstractions, Core
AspNetCore     →  Abstractions, Core
Diagnostics    →  Abstractions, Core
Analyzers      →  (build-time, no runtime references)
Generators     →  (build-time, no runtime references)
```

Cross-provider references are forbidden:
- `MicroKit.Logging.Serilog` must not reference `MicroKit.Logging.OpenTelemetry`
- `MicroKit.Logging.OpenTelemetry` must not reference `MicroKit.Logging.Serilog`

## Adding a New Dependency

Before adding any new `PackageReference`:
1. Check it is not already available transitively
2. Add it to `Directory.Packages.props` first
3. Verify the package is compatible with .NET 10
4. If adding to `Abstractions`: requires explicit approval — open a discussion first
