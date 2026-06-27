# Workflow: Multitenancy Releasing MicroKit.Tenancy

## Trigger
```
/tenancy-release
```
This invokes the `tenancy-release-manager` agent.

## Pre-release requirements

```
□ All tests green: dotnet test modules/MicroKit.Tenancy/MicroKit.Tenancy.slnx -c Release
□ CHANGELOG.md updated with release notes under ## [{version}] — {date}
□ No uncommitted changes in modules/MicroKit.Tenancy/
□ PR merged to main
```

## Release branch workflow

```bash
git checkout dev && git pull
git checkout -b release/tenancy/{version}

# Finalize CHANGELOG.md
git commit -m "chore(multitenancy): prepare release {version}"

# PR to main
# After merge on main:
git tag tenancy-v{version} -m "MicroKit.Tenancy {version}"
git push origin tenancy-v{version}
```

## What GitHub Actions does

`release-multitenancy.yml` on `tenancy-v*` tag:
1. Extracts `PACKAGE_VERSION` from tag (strips `tenancy-v` prefix)
2. Builds and tests
3. `dotnet pack -p:PackageVersion=$PACKAGE_VERSION -o nupkgs`
4. Pushes all 5 `.nupkg` to NuGet.org

## Post-release

```bash
# Back-merge to dev
git checkout dev
git merge main --ff-only
git push origin dev

# Update Directory.Packages.props with new version if used by other modules
```
