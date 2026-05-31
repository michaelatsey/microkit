---
name: release
description: How to release the 4 MicroKit.MediatR NuGet packages. Use whenever you need to prepare a release, validate the build/changelog/package graph, compute the version from Nerdbank.GitVersioning, create the mediatr-v* git tag, or publish to NuGet. Delegates to the release-manager agent via /release.
---

# Skill: Release

How to release the 4 MicroKit.MediatR packages.

## Packages (4)

- `MicroKit.MediatR.Abstractions`
- `MicroKit.MediatR`
- `MicroKit.MediatR.Behaviors`
- `MicroKit.MediatR.Testing`

All share one version per release (Nerdbank.GitVersioning).

## The Flow

The `/release` command delegates to the `release-manager` agent. Do not run the steps manually
unless debugging — the agent enforces the abort conditions.

```
/release [--version <semver>] [--pre <label>]
```

## Pre-flight (must all pass)

- On `main` or `release/mediatr-*`
- CI green, no uncommitted changes
- `CHANGELOG.md` has an entry for the version
- `dependency-guardian` passes (no FluentValidation/Polly leak, no FluentAssertions, no inline versions)

## Build, Test, Pack

```bash
dotnet build modules/MicroKit.MediatR/MicroKit.MediatR.slnx -c Release
dotnet test  modules/MicroKit.MediatR/MicroKit.MediatR.slnx -c Release --no-build
dotnet pack  modules/MicroKit.MediatR/MicroKit.MediatR.slnx -c Release --no-build -o artifacts/mediatr/
```

## Package Validation

- All 4 `.nupkg` + 4 `.snupkg` present
- `Abstractions` deps = `MediatR.Contracts`, `MicroKit.Domain.Abstractions`, `MicroKit.Logging.Abstractions`, `MicroKit.Result`
- `Behaviors` deps include `FluentValidation` + `Polly`
- `Testing` deps include `NSubstitute`
- No `FluentAssertions` anywhere

## Tag (triggers publish)

```bash
git tag mediatr-v{version} -m "MicroKit.MediatR v{version}"
git push origin mediatr-v{version}
```

The tag push triggers `release.yml` → NuGet publish.

## Rollback

NuGet packages cannot be deleted, only unlisted:

```bash
dotnet nuget delete MicroKit.MediatR.Abstractions {version} --source nuget.org
```

Then ship a patch (`mediatr-v{major}.{minor}.{patch+1}`).
