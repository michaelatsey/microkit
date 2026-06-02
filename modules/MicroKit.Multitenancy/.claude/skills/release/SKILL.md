# Skill: Release — MicroKit.Multitenancy

## Trigger via `/release` command

The release-manager agent orchestrates the full lifecycle.

## Manual steps (if needed)

```bash
# 1. Verify all tests pass
dotnet test modules/MicroKit.Multitenancy/MicroKit.Multitenancy.slnx -c Release

# 2. Pack locally to verify
dotnet pack modules/MicroKit.Multitenancy/MicroKit.Multitenancy.slnx \
  -c Release \
  -p:PackageVersion=1.0.0-preview.1 \
  -o /tmp/multitenancy-nupkgs

# Verify all 5 packages are produced
ls /tmp/multitenancy-nupkgs/

# 3. Push tag to trigger GitHub Actions
git tag multitenancy-v1.0.0-preview.1 -m "MicroKit.Multitenancy 1.0.0-preview.1"
git push origin multitenancy-v1.0.0-preview.1
```

## Tag convention
```
multitenancy-v{major}.{minor}.{patch}[-{prerelease}]
multitenancy-v1.0.0-preview.1
multitenancy-v1.0.0
```
