# Rule: Packaging — MicroKit.Persistence

## Package IDs

| Project | NuGet Package ID |
|---------|-----------------|
| `MicroKit.Persistence.Abstractions` | `MicroKit.Persistence.Abstractions` |
| `MicroKit.Persistence` | `MicroKit.Persistence` |
| `MicroKit.Persistence.EntityFrameworkCore` | `MicroKit.Persistence.EntityFrameworkCore` |
| `MicroKit.Persistence.EntityFrameworkCore.PostgreSql` | `MicroKit.Persistence.EntityFrameworkCore.PostgreSql` |
| `MicroKit.Persistence.EntityFrameworkCore.SqlServer` | `MicroKit.Persistence.EntityFrameworkCore.SqlServer` |
| `MicroKit.Persistence.Specifications` | `MicroKit.Persistence.Specifications` |
| `MicroKit.Persistence.Testing` | `MicroKit.Persistence.Testing` |
| `MicroKit.Persistence.Analyzers` | `MicroKit.Persistence.Analyzers` |

## Versioning

Versions are computed by **Nerdbank.GitVersioning** from `version.json`:

```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/persistence-v\\d+\\.\\d+"
  ]
}
```

All 8 packages share the same version within a release.

## Required Package Metadata (.csproj)

```xml
<Authors>Ange-Michaël Atsé</Authors>
<Description>...</Description>
<PackageTags>persistence;repository;ef-core;unit-of-work;microkit;ddd;dotnet</PackageTags>
<PackageProjectUrl>https://github.com/michaelatsey/microkit</PackageProjectUrl>
<RepositoryUrl>https://github.com/michaelatsey/microkit</RepositoryUrl>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```

## Analyzers Package Special Configuration

The Analyzers package must be referenced as a build-time tool, not a runtime dependency:

```xml
<!-- In consuming project .csproj -->
<PackageReference Include="MicroKit.Persistence.Analyzers">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

The Analyzers `.nupkg` must contain `analyzers/dotnet/cs/` — no `lib/` folder.

## Release Tag Convention

```
persistence-v1.0.0
persistence-v1.1.0-beta.1
persistence-v2.0.0
```

## Declared NuGet Dependencies per Package

| Package | Runtime Dependencies |
|---------|---------------------|
| `Abstractions` | `MicroKit.Result`, `MicroKit.Domain.Abstractions` |
| `MicroKit.Persistence` | `MicroKit.Persistence.Abstractions`, `MicroKit.Logging.Abstractions` |
| `EntityFrameworkCore` | `MicroKit.Persistence`, `Microsoft.EntityFrameworkCore` |
| `PostgreSql` | `MicroKit.Persistence.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` |
| `SqlServer` | `MicroKit.Persistence.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer` |
| `Specifications` | `MicroKit.Persistence` |
| `Testing` | `MicroKit.Persistence`, `NSubstitute` |
| `Analyzers` | `Microsoft.CodeAnalysis.CSharp` (build-time, not runtime) |
