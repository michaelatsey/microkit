# Rule: Packaging — MicroKit.Persistence

## Packages

Ten packages, one per `src/` project, package ID = project name:

```
MicroKit.Persistence.Abstractions
MicroKit.Persistence
MicroKit.Persistence.EntityFrameworkCore
MicroKit.Persistence.EntityFrameworkCore.PostgreSql
MicroKit.Persistence.EntityFrameworkCore.SqlServer
MicroKit.Persistence.Specifications
MicroKit.Persistence.Testing
MicroKit.Persistence.Testing.PostgreSql
MicroKit.Persistence.Testing.SqlServer
MicroKit.Persistence.Analyzers
```

Tests and `samples/MicroKit.Persistence.Samples` are `IsPackable=false`.

## Metadata

Common metadata (`Authors`, project and repository URLs, `MIT` licence, symbols as `snupkg`) comes
from the root `Directory.Build.props`. Each `.csproj` sets its own `Description` and
`PackageTags`; keep the description true to what the package contains.

## Versioning and Tags

All 10 packages ship under one version. A `persistence-v<semver>` tag (e.g.
`persistence-v1.0.0-preview.4`) triggers `.github/workflows/release-persistence.yml`, which takes
the version from the tag. `version.json` is not read by the build. ADR-GLOBAL-002 (root) defines
the replacement model; no tag is cut until steps 1–4 of its migration land.

## Declared NuGet Dependencies

| Package | Dependencies |
|---------|--------------|
| `Abstractions` | `MicroKit.Domain` |
| `MicroKit.Persistence` | `MicroKit.Persistence.Abstractions`, `MicroKit.Result` |
| `EntityFrameworkCore` | `MicroKit.Persistence`, `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `PostgreSql` | `MicroKit.Persistence.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` |
| `SqlServer` | `MicroKit.Persistence.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer` |
| `Specifications` | `MicroKit.Persistence` |
| `Testing` | `MicroKit.Persistence` |
| `Testing.PostgreSql` | `MicroKit.Persistence.Testing`, `Testcontainers.PostgreSql` — no test framework (`PackageClosureTests`) |
| `Testing.SqlServer` | `MicroKit.Persistence.Testing`, `Testcontainers.MsSql` — no test framework (`PackageClosureTests`) |
| `Analyzers` | none — `Microsoft.CodeAnalysis.CSharp` is referenced with `PrivateAssets="all"`; the compiler provides Roslyn |

## Analyzers Package

`netstandard2.0`, `IncludeBuildOutput=false`, the DLL packed to `analyzers/dotnet/cs/` — no `lib/`.
Consumers:

```xml
<PackageReference Include="MicroKit.Persistence.Analyzers">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

## Before Publishing

Pack to `artifacts/persistence/` and consume the packages from a local folder feed in a real
consumer before any NuGet.org publish (see `workflows/releasing-module.md`).
