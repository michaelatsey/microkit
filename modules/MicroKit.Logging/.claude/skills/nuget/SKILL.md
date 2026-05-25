# Skill: NuGet

How to manage packages, validate dependencies, and work with NuGet in MicroKit.Logging.

## Central Package Management

All versions in `build/Directory.Packages.props`. Never add `Version=` to a `.csproj`.

```bash
# View all package versions
cat build/Directory.Packages.props

# Check for outdated packages
dotnet list modules/MicroKit.Logging/MicroKit.Logging.slnx package --outdated

# Check for vulnerable packages
dotnet list modules/MicroKit.Logging/MicroKit.Logging.slnx package --vulnerable
```

## Adding a New Package

```bash
# 1. Add to Directory.Packages.props
# 2. Add PackageReference (no Version=) to the target .csproj
# 3. Restore
dotnet restore modules/MicroKit.Logging/MicroKit.Logging.slnx
# 4. Run dependency-guardian check
```

## Inspecting a Package

```bash
# View dependencies of a generated package
dotnet nuget inspect artifacts/logging/MicroKit.Logging.Abstractions.*.nupkg

# Check what's inside
unzip -l artifacts/logging/MicroKit.Logging.Abstractions.*.nupkg
```

## Local Testing

Test a package locally before pushing:

```bash
# Add local feed
dotnet nuget add source ./artifacts/logging/ --name MicroKitLocal

# Reference from a test project
dotnet add package MicroKit.Logging.Abstractions --source MicroKitLocal --prerelease
```

## Symbol Packages

Every package must produce a `.snupkg` symbol package:

```xml
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```
