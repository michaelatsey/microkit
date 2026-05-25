# Rule: Packaging

## Package IDs

| Project | NuGet Package ID |
|---------|-----------------|
| `MicroKit.Logging.Abstractions` | `MicroKit.Logging.Abstractions` |
| `MicroKit.Logging` | `MicroKit.Logging` |
| `MicroKit.Logging.OpenTelemetry` | `MicroKit.Logging.OpenTelemetry` |
| `MicroKit.Logging.Serilog` | `MicroKit.Logging.Serilog` |
| `MicroKit.Logging.AspNetCore` | `MicroKit.Logging.AspNetCore` |
| `MicroKit.Logging.Diagnostics` | `MicroKit.Logging.Diagnostics` |
| `MicroKit.Logging.Analyzers` | `MicroKit.Logging.Analyzers` |
| `MicroKit.Logging.Generators` | `MicroKit.Logging.Generators` |

## Versioning

Versions are computed by **Nerdbank.GitVersioning** from `version.json`:

```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/logging-v\\d+\\.\\d+"
  ]
}
```

All 8 packages share the same version within a release. Independent versioning per package is not supported in v1.

## Required Package Metadata (.csproj)

```xml
<Authors>Ange-Michaël Atsé</Authors>
<Description>...</Description>
<PackageTags>logging;microkit;observability;dotnet</PackageTags>
<PackageProjectUrl>https://github.com/michaelatsey/microkit</PackageProjectUrl>
<RepositoryUrl>https://github.com/michaelatsey/microkit</RepositoryUrl>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```

## Analyzers and Generators as Build-Time Dependencies

`MicroKit.Logging.Analyzers` and `MicroKit.Logging.Generators` are packaged with:

```xml
<IncludeBuildOutput>false</IncludeBuildOutput>
```

They are referenced via `<PackageReference>` with `PrivateAssets="all"` by consumers:

```xml
<PackageReference Include="MicroKit.Logging.Analyzers" PrivateAssets="all" />
```

## Release Tag Convention

```
logging-v1.0.0
logging-v1.1.0-beta.1
logging-v2.0.0
```

Tag format: `logging-v{semver}` — this triggers `release.yml` in GitHub Actions.
