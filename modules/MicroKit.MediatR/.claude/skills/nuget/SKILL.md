---
name: nuget
description: How to manage NuGet packages, Central Package Management, and dependency validation in MicroKit.MediatR. Use whenever you add/inspect a package, check for outdated/vulnerable deps, verify a package graph before publish, or test a package locally. All versions live in Directory.Packages.props — never in a .csproj.
---

# Skill: NuGet

How to manage packages, validate dependencies, and work with NuGet in MicroKit.MediatR.

## Central Package Management

All versions live in `build/Directory.Packages.props`. Never add `Version=` to a `.csproj`.

```bash
# View all package versions
cat build/Directory.Packages.props

# Check for outdated packages
dotnet list modules/MicroKit.MediatR/MicroKit.MediatR.slnx package --outdated

# Check for vulnerable packages
dotnet list modules/MicroKit.MediatR/MicroKit.MediatR.slnx package --vulnerable
```

## Adding a New Package

```
1. Add <PackageVersion> to build/Directory.Packages.props
2. Add <PackageReference> (no Version=) to the correct layer:
     - resilience / validation → Behaviors
     - mocking → Testing
     - dispatch / DI → core
     - contracts → Abstractions (requires api-reviewer approval)
3. dotnet restore modules/MicroKit.MediatR/MicroKit.MediatR.slnx
4. Run dependency-guardian (or /review-architecture)
```

## Banned / Confined Packages

| Package | Status |
|---------|--------|
| `FluentAssertions` | **Banned everywhere** (commercial license) — use Shouldly |
| `FluentValidation`, `Polly` | Confined to `MicroKit.MediatR.Behaviors` |
| `NSubstitute` | Confined to `MicroKit.MediatR.Testing` |
| `MediatR` (engine) | Core only — Abstractions uses `MediatR.Contracts` |

## Inspecting a Package

```bash
dotnet nuget inspect artifacts/mediatr/MicroKit.MediatR.Abstractions.*.nupkg
unzip -l artifacts/mediatr/MicroKit.MediatR.Abstractions.*.nupkg
```

## Local Testing

```bash
dotnet nuget add source ./artifacts/mediatr/ --name MicroKitLocal
dotnet add package MicroKit.MediatR --source MicroKitLocal --prerelease
```

## Symbol Packages

Every package must produce a `.snupkg`:

```xml
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```
