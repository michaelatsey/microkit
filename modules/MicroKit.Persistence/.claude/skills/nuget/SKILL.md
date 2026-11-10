---
name: nuget
description: How to manage NuGet packages and CPM for MicroKit.Persistence. Use when adding or inspecting packages, verifying the declared dependency graph, packing locally, or checking for outdated or vulnerable dependencies. All versions live in the root Directory.Packages.props.
---

# Skill: NuGet

## Central Package Management

Versions live in the root `Directory.Packages.props` (`ManagePackageVersionsCentrally`,
`CentralPackageTransitivePinningEnabled`).

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.9" />

<!-- .csproj — never a Version= -->
<PackageReference Include="Microsoft.EntityFrameworkCore" />
```

Which package may go where: `.claude/rules/dependencies.md`.

## Inspect

```bash
dotnet list modules/MicroKit.Persistence/MicroKit.Persistence.slnx package --outdated
dotnet list modules/MicroKit.Persistence/MicroKit.Persistence.slnx package --vulnerable --include-transitive
dotnet list modules/MicroKit.Persistence/MicroKit.Persistence.slnx package --include-transitive
```

## Pack and consume locally

```bash
dotnet pack modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release \
  -p:PackageVersion=1.0.0-preview.local -o artifacts/persistence/

dotnet nuget add source "$PWD/artifacts/persistence" --name local-persistence
dotnet add <consumer>.csproj package MicroKit.Persistence.EntityFrameworkCore \
  --version 1.0.0-preview.local --source local-persistence
```

Check each `.nuspec` (rename the `.nupkg` to `.zip`, or `unzip -p x.nupkg '*.nuspec'`) against the
declared dependencies in `.claude/rules/packaging.md`; the Analyzers package must have
`analyzers/dotnet/cs/` and no `lib/`.

## Confinement

| Package | Allowed in |
|---------|-----------|
| `Microsoft.EntityFrameworkCore*` | EntityFrameworkCore and the providers |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSql |
| `Microsoft.EntityFrameworkCore.SqlServer` | SqlServer |
| `NSubstitute` | test projects |
| `FluentAssertions` | nowhere |
