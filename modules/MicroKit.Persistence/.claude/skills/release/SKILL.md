---
name: release
description: How to release MicroKit.Persistence — all 8 packages simultaneously. Use whenever preparing a release, bumping versions, tagging, or publishing to NuGet.
---

# Skill: Release

How to release all 8 MicroKit.Persistence packages.

## Quick Release

```
/release
```

The `release-manager` agent orchestrates all steps.

## Manual Steps

```bash
# 1. Build Release
dotnet build modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release

# 2. Run full tests
dotnet test modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release --no-build

# 3. Pack
dotnet pack modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release -o /tmp/prd-pack/

# 4. Verify 8 packages
ls /tmp/prd-pack/*.nupkg | wc -l  # should be 8

# 5. Tag
git tag persistence-v{version} -m "MicroKit.Persistence {version}"
git push origin persistence-v{version}
```

## Tag Convention

```
persistence-v1.0.0
persistence-v1.0.0-preview.1
persistence-v1.1.0
```

## CI/CD

Tag push triggers `release-persistence.yml` which publishes all 8 packages to NuGet.org.

```bash
gh run list --workflow=release-persistence.yml --limit=5
```
