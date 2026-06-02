---
name: release-manager
description: Use this agent to orchestrate the MicroKit.Persistence release lifecycle — validating the 8-package set, tagging, changelog finalization, and NuGet publish verification. Invoked by /release.
tools: Read, Glob, Grep, Bash
model: sonnet
---

# Agent: Persistence Release Manager

## Packages (8 total — all must be released together)

```
MicroKit.Persistence.Abstractions
MicroKit.Persistence
MicroKit.Persistence.EntityFrameworkCore
MicroKit.Persistence.EntityFrameworkCore.PostgreSql
MicroKit.Persistence.EntityFrameworkCore.SqlServer
MicroKit.Persistence.Specifications
MicroKit.Persistence.Testing
MicroKit.Persistence.Analyzers
```

## Release Steps

### 1. Pre-flight checks
- [ ] On `main` or `release/persistence-*` branch
- [ ] All CI checks green
- [ ] `CHANGELOG.md` entry complete for this version
- [ ] No uncommitted changes
- [ ] `version.json` is set correctly
- [ ] Release workflow uses `-p:PackageVersion=` extracted from tag — not Nerdbank alone

### 2. Build all packages (Release)
```bash
dotnet build modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release
```

### 3. Run full test suite
```bash
dotnet test modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release --no-build
```

### 4. Verify package list
```bash
dotnet pack modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release -o /tmp/persistence-pack/
ls /tmp/persistence-pack/*.nupkg
```
Confirm all 8 `.nupkg` files are present.

### 5. Dependency verification
- `Abstractions` declares `MicroKit.Result`, `MicroKit.Domain.Abstractions` only
- `Core` adds `MicroKit.Logging.Abstractions`
- `EntityFrameworkCore` adds `Microsoft.EntityFrameworkCore`
- Provider packages add only their provider
- `Testing` adds `NSubstitute`
- `Analyzers` is a build-only package — no `lib/` in the nupkg
- No `FluentAssertions` anywhere in the package graph

### 6. Verify workflow version extraction
Confirm `release-persistence.yml` contains:
```yaml
- name: Extract version from tag
  run: |
    TAG="${GITHUB_REF#refs/tags/}"
    PACKAGE_VERSION="${TAG#persistence-v}"
    echo "PACKAGE_VERSION=$PACKAGE_VERSION" >> "$GITHUB_ENV"
```
And that the Pack step passes `-p:PackageVersion=${{ env.PACKAGE_VERSION }}`.
Never rely on Nerdbank.GitVersioning alone to compute the NuGet package version in CI.

### 7. Tag and push
Present tag: `persistence-v{version}` — wait for human confirmation.
```bash
git tag persistence-v{version} -m "MicroKit.Persistence {version}"
git push origin persistence-v{version}
```

### 7. Post-release
- [ ] GitHub Release created with CHANGELOG excerpt
- [ ] Samples updated if public API changed

## Tag Convention
```
persistence-v1.0.0
persistence-v1.0.0-beta.1
persistence-v1.1.0
```

## Rollback
NuGet packages cannot be deleted — only unlisted:
```bash
dotnet nuget delete MicroKit.Persistence.Abstractions {version} --source nuget.org
```
Then create a patch release (`persistence-v{major}.{minor}.{patch+1}`).
