---
name: nuget
description: How to manage NuGet packages and CPM for MicroKit.Persistence. Use when adding/inspecting packages, verifying the dependency graph, or checking for outdated/vulnerable deps. All versions live in Directory.Packages.props.
---

# Skill: NuGet

How to manage packages in MicroKit.Persistence.

## Central Package Management (CPM)

All package versions are in `build/Directory.Packages.props` at the monorepo root.

```xml
<!-- ✅ Version in Directory.Packages.props -->
<PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.x.x" />

<!-- ✅ Reference without version in .csproj -->
<PackageReference Include="Microsoft.EntityFrameworkCore" />

<!-- ❌ Version in .csproj — CPM violation blocked by dependency-check hook -->
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.x.x" />
```

## Check for Outdated Packages

```bash
dotnet list modules/MicroKit.Persistence/MicroKit.Persistence.slnx package --outdated
```

## Check for Vulnerable Packages

```bash
dotnet list modules/MicroKit.Persistence/MicroKit.Persistence.slnx package --vulnerable
```

## Verify Package Graph Before Publish

```bash
dotnet pack modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release -o /tmp/check/
nuget verify /tmp/check/*.nupkg
```

## Test Package Locally

```bash
dotnet nuget add source /tmp/prd-pack/ --name local-persistence
dotnet add package MicroKit.Persistence.Abstractions --source local-persistence
```

## Package Confinement Rules

| Package | Allowed In | Blocked From |
|---------|-----------|-------------|
| `Microsoft.EntityFrameworkCore` | EntityFrameworkCore | Abstractions, Core |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSql only | All others |
| `Microsoft.EntityFrameworkCore.SqlServer` | SqlServer only | All others |
| `NSubstitute` | Testing only | All runtime packages |
| `FluentAssertions` | **Banned everywhere** | Commercial license |
