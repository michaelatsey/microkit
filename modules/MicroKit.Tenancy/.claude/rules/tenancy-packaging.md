# Rule: Packaging — MicroKit.Tenancy

## Package IDs

| Project | NuGet Package ID |
|---------|-----------------|
| `MicroKit.Tenancy.Abstractions` | `MicroKit.Tenancy.Abstractions` |
| `MicroKit.Tenancy` | `MicroKit.Tenancy` |
| `MicroKit.Tenancy.AspNetCore` | `MicroKit.Tenancy.AspNetCore` |
| `MicroKit.Tenancy.EntityFrameworkCore` | `MicroKit.Tenancy.EntityFrameworkCore` |
| `MicroKit.Tenancy.Analyzers` | `MicroKit.Tenancy.Analyzers` |

## Versioning

Versions are computed by **Nerdbank.GitVersioning** from `version.json`.
All 5 packages share the same version within a release.

```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/tenancy-v\\d+\\.\\d+"
  ]
}
```

## Required Package Metadata (.csproj)

```xml
<Authors>Ange-Michaël Atsé</Authors>
<Description>...</Description>
<PackageTags>multitenancy;tenant;aspnetcore;ef-core;microkit;ddd;dotnet</PackageTags>
<PackageProjectUrl>https://github.com/michaelatsey/microkit</PackageProjectUrl>
<RepositoryUrl>https://github.com/michaelatsey/microkit</RepositoryUrl>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```

## Analyzers Package Special Configuration

```xml
<!-- In consuming project .csproj -->
<PackageReference Include="MicroKit.Tenancy.Analyzers">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

The Analyzers `.nupkg` must contain `analyzers/dotnet/cs/` — no `lib/` folder.

## Release Tag Convention

```
tenancy-v1.0.0
tenancy-v1.0.1
tenancy-v1.1.0-beta.1
tenancy-v2.0.0
```

## CIReleaseBuild Pattern

Cross-module `ProjectReference` in dev builds must switch to `PackageReference` for release:

```xml
<ItemGroup Condition="'$(CIReleaseBuild)' != 'true'">
  <ProjectReference Include="../../../MicroKit.Result/src/MicroKit.Result/MicroKit.Result.csproj" />
</ItemGroup>
<ItemGroup Condition="'$(CIReleaseBuild)' == 'true'">
  <PackageReference Include="MicroKit.Result" />
</ItemGroup>
```

## Declared NuGet Dependencies per Package

| Package | Runtime Dependencies |
|---------|---------------------|
| `Abstractions` | `MicroKit.Result` |
| `MicroKit.Tenancy` | `MicroKit.Tenancy.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `AspNetCore` | `MicroKit.Tenancy` (framework-provided: ASP.NET Core) |
| `EntityFrameworkCore` | `MicroKit.Tenancy`, `MicroKit.Persistence.Abstractions`, `MicroKit.Persistence.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore` |
| `Analyzers` | `Microsoft.CodeAnalysis.CSharp` (build-time, not runtime) |
