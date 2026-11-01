---
name: release-manager
description: Use this agent to orchestrate the MicroKit.Result release lifecycle — validating the 2-package set, tagging, changelog finalization, and NuGet publish verification. Invoked by /release.
tools: Read, Glob, Grep, Bash
model: sonnet
---

# Agent: Result Release Manager

**Status (ADR-GLOBAL-002):** no tag is cut until the ADR's migration steps 1–4 land; `eng/Versions.props`
does not exist yet. The steps below describe the current `release-result.yml`, whose `result-v*` tags
D8 replaces with one `v<semver>` tag.

## Packages (2 total — all must be released together)

```
MicroKit.Result
MicroKit.Result.AspNetCore
```

## Release Steps

### 1. Pre-flight checks
- [ ] On `main` (ADR-GLOBAL-003: the only long-lived branch; a release is a tag on it)
- [ ] All CI checks green
- [ ] `CHANGELOG.md` entry complete for this version
- [ ] No uncommitted changes
- [ ] Release workflow uses `-p:PackageVersion=` extracted from tag

### 2. Build all packages (Release)
```bash
dotnet build modules/MicroKit.Result/MicroKit.Result.slnx -c Release
```

### 3. Run full test suite
```bash
dotnet test modules/MicroKit.Result/MicroKit.Result.slnx -c Release --no-build
```

### 4. Verify package list
```bash
dotnet pack modules/MicroKit.Result/MicroKit.Result.slnx -c Release -o /tmp/result-pack/
ls /tmp/result-pack/*.nupkg
```
Confirm both `.nupkg` files are present.

### 5. Dependency verification
- `MicroKit.Result` declares no MicroKit dependencies — it is a Level 0 foundation package
- `MicroKit.Result.AspNetCore` declares `MicroKit.Result` + the `Microsoft.AspNetCore.App` framework reference only
- No banned test library anywhere in the package graph (root rule `.claude/rules/testing-libraries.md`)

### 6. Verify workflow version extraction
Confirm `release-result.yml` contains:
```yaml
- name: Extract version from tag
  run: |
    TAG="${GITHUB_REF#refs/tags/}"
    PACKAGE_VERSION="${TAG#result-v}"
    echo "PACKAGE_VERSION=$PACKAGE_VERSION" >> "$GITHUB_ENV"
```
And that the Pack step passes `-p:PackageVersion=${{ env.PACKAGE_VERSION }}`.
This prefix-strip is the only live version computation: Nerdbank.GitVersioning is not installed and
`version.json` is read by nothing (ADR-GLOBAL-002).

### 7. Tag and push
Blocked until ADR-GLOBAL-002 steps 1–4 land (see Status above).
Present tag: `result-v{version}` — wait for human confirmation.
```bash
git tag result-v{version} -m "MicroKit.Result {version}"
git push origin result-v{version}
```

### 8. Post-release
- [ ] GitHub Release created with CHANGELOG excerpt
- [ ] Samples updated if public API changed
- [ ] Notify dependent modules (MicroKit.Auth, MicroKit.MediatR, MicroKit.Messaging, MicroKit.Persistence, MicroKit.Tenancy) if Result<T> signatures changed

## Tag Convention
The form `release-result.yml` triggers on today. ADR-GLOBAL-002 replaces it with `v2.0.0-preview.1`,
`v2.0.0` (D8: no module prefix, `preview.N` only; D3: `2.0.0` is the boundary).
```
result-v2.0.0-preview.1
result-v2.0.0
```

## Rollback
NuGet packages cannot be deleted — only unlisted:
```bash
dotnet nuget delete MicroKit.Result {version} --source nuget.org
```
Then create a patch release (`result-v{major}.{minor}.{patch+1}`).
