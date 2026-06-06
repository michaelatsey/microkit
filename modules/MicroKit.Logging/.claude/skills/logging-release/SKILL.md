# Skill: Logging Release

How to prepare and execute a MicroKit.Logging release. For automated execution, use the `/logging-release` command which delegates to `release-manager` agent.

## Manual Release Steps

### 1. Verify State

```bash
git status --porcelain           # Must be clean
git branch --show-current        # Must be main or release/logging-*
gh run list --workflow=ci-logging.yml --limit=1   # Last run must be green
```

### 2. Check Version

```bash
# Preview version from Nerdbank.GitVersioning
dotnet nbgv get-version --project modules/MicroKit.Logging/
```

### 3. Build and Test

```bash
dotnet build modules/MicroKit.Logging/MicroKit.Logging.slnx -c Release
dotnet test modules/MicroKit.Logging/MicroKit.Logging.slnx -c Release --no-build
```

### 4. Pack

```bash
dotnet pack modules/MicroKit.Logging/MicroKit.Logging.slnx \
  -c Release --no-build \
  -o artifacts/logging/
```

### 5. Validate Packages

```bash
# List produced packages
ls artifacts/logging/*.nupkg

# Validate with NuGet inspector
dotnet nuget verify artifacts/logging/*.nupkg
```

### 6. Tag and Push

```bash
VERSION=$(dotnet nbgv get-version --project modules/MicroKit.Logging/ --format json | jq -r '.NuGetPackageVersion')
git tag "logging-v$VERSION" -m "MicroKit.Logging v$VERSION"
git push origin "logging-v$VERSION"
```

Tag push triggers `release.yml` → NuGet publish.

## Changelog Entry Format

```markdown
## [1.2.0] - 2026-05-24

### Added
- `ILogEnricher` context-aware overload

### Changed
- `EnrichmentPipeline` now supports ordered enrichers

### Fixed
- `CorrelationId` not propagated across `ConfigureAwait(false)` boundaries
```
