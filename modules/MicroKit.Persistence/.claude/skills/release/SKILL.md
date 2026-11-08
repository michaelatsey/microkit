---
name: release
description: How to release MicroKit.Persistence — all 8 packages under one version. Use whenever preparing a release, choosing a version, packing, tagging, or publishing to NuGet.
---

# Skill: Release

The root `/release Persistence` command runs the `release-manager` agent; the full checklist is
`.claude/workflows/releasing-module.md`. ADR-GLOBAL-002 (root) governs releases; no tag is cut
until steps 1–4 of its migration land.

## Manual steps

```bash
# 1. Build and test (Docker available, so the PostgreSQL tests run)
dotnet build modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release
dotnet test  modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release --no-build

# 2. Pack with the release version, check the 8 packages, consume them from a local feed
dotnet pack modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release \
  -p:PackageVersion={version} -o artifacts/persistence/
ls artifacts/persistence/*.nupkg | wc -l   # 8

# 3. Tag — after the human confirms
git tag persistence-v{version} -m "MicroKit.Persistence {version}"
git push origin persistence-v{version}
```

## Versions and tags

`persistence-v1.0.0-preview.N` while on preview (`preview.N`, never `alpha`/`beta`/`rc` — ADR-GLOBAL-002 D8), then
`persistence-v1.0.0`. The tag is the version: `release-persistence.yml` strips the
`persistence-v` prefix and passes it as `-p:PackageVersion`. `version.json` is not read.

## CI

The tag push runs `.github/workflows/release-persistence.yml`: restore and build with
`CIReleaseBuild=true` (MicroKit.Domain from NuGet), test, pack, push to NuGet.org.

```bash
gh run list --workflow=release-persistence.yml --limit=5
```
