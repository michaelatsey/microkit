# Rule: Packaging — MicroKit.MediatR

## Package IDs

| Project | NuGet Package ID |
|---------|-----------------|
| `MicroKit.MediatR.Abstractions` | `MicroKit.MediatR.Abstractions` |
| `MicroKit.MediatR` | `MicroKit.MediatR` |
| `MicroKit.MediatR.Behaviors` | `MicroKit.MediatR.Behaviors` |
| `MicroKit.MediatR.Testing` | `MicroKit.MediatR.Testing` |

## Versioning

Versions are computed by **Nerdbank.GitVersioning** from `version.json`:

```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/mediatr-v\\d+\\.\\d+"
  ]
}
```

All 4 packages share the same version within a release. Independent versioning per package is not supported in v1.

## Required Package Metadata (.csproj)

```xml
<Authors>Ange-Michaël Atsé</Authors>
<Description>...</Description>
<PackageTags>mediatr;cqrs;microkit;mediator;ddd;dotnet</PackageTags>
<PackageProjectUrl>https://github.com/michaelatsey/microkit</PackageProjectUrl>
<RepositoryUrl>https://github.com/michaelatsey/microkit</RepositoryUrl>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```

## Dependency Declarations per Package

| Package | Declared NuGet dependencies |
|---------|----------------------------|
| `Abstractions` | `MediatR.Contracts`, `MicroKit.Domain.Abstractions`, `MicroKit.Logging.Abstractions`, `MicroKit.Result` |
| `MicroKit.MediatR` | `MicroKit.MediatR.Abstractions`, `MediatR`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `Behaviors` | `MicroKit.MediatR`, `FluentValidation`, `Polly`, `MicroKit.Logging.Abstractions` |
| `Testing` | `MicroKit.MediatR`, `NSubstitute` |

> `FluentAssertions` must never appear in any package graph (banned — commercial license).

## Release Tag Convention

```
mediatr-v1.0.0
mediatr-v1.1.0-beta.1
mediatr-v2.0.0
```

Tag format: `mediatr-v{semver}` — this triggers `release.yml` in GitHub Actions.

## Pre-release Channel

Pre-release packages use the `-beta.N` / `-preview.N` suffix and are published only from a
`release/mediatr-*` branch or a pre-release tag. Stable consumers must not pin a pre-release of
MicroKit.MediatR (see root `.claude/rules/module-boundaries.md`).
