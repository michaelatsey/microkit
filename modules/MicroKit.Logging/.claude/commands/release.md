# /release

Prepare and execute a release of MicroKit.Logging packages.

## Usage

```
/release [--version <semver>] [--pre <label>]
```

**Examples:**
```
/release
/release --version 1.2.0
/release --version 1.2.0 --pre beta.1
```

## What This Command Does

Delegates the full release lifecycle to the `release-manager` agent.

## Steps

```
1. Use agent release-manager
2. Agent performs pre-flight checks (branch, CI status, changelog)
3. Agent validates build and tests
4. Agent packs all 8 packages
5. Agent validates package graph
6. Present summary for approval before tagging
7. On approval: agent creates and pushes tag
8. Tag push triggers GitHub Actions release.yml → NuGet publish
```

## Version Resolution

If `--version` is not provided, the version is computed from `Nerdbank.GitVersioning` based on `version.json` and the current commit height.

If `--pre` is provided, packages will have the pre-release suffix: `1.2.0-beta.1`.

## Abort Conditions

The command will **not proceed** if:
- Any test is failing
- `CHANGELOG.md` has no entry for the target version
- Not on `main` or `release/logging-*` branch
- `dependency-guardian` check fails

## Tag Convention

```
logging-v1.0.0
logging-v1.1.0-beta.1
```
