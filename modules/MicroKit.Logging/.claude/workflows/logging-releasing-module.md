# Workflow: Logging Releasing a Module

End-to-end release process for MicroKit.Logging packages.

## Prerequisites

- [ ] On `main` or `release/logging-*` branch
- [ ] All CI checks green
- [ ] `CHANGELOG.md` entry complete for this version
- [ ] No uncommitted changes

## Steps

### 1. Run the Release Command

```
/release
```

The `release-manager` agent handles all steps from here. Review each step's output before approving.

### 2. Review Package List

Verify all 8 packages are present:
- `MicroKit.Logging.Abstractions`
- `MicroKit.Logging`
- `MicroKit.Logging.OpenTelemetry`
- `MicroKit.Logging.Serilog`
- `MicroKit.Logging.AspNetCore`
- `MicroKit.Logging.Diagnostics`
- `MicroKit.Logging.Analyzers`
- `MicroKit.Logging.Generators`

### 3. Approve Tag Creation

The agent will present the tag (`logging-v{version}`) and ask for confirmation before pushing.

### 4. Verify GitHub Actions

```bash
gh run list --workflow=release.yml --limit=3
```

The `release.yml` workflow publishes all packages to NuGet.org automatically on tag push.

### 5. Post-Release

- [ ] GitHub Release created with changelog excerpt
- [ ] Announce in relevant channels if a major/minor release
- [ ] Update samples if API changed

## Rollback

If a bad package is published:
```bash
# NuGet packages cannot be deleted — they can only be unlisted
dotnet nuget delete MicroKit.Logging.Abstractions {version} --source nuget.org
```

Create a patch release (`logging-v{major}.{minor}.{patch+1}`) with the fix.
