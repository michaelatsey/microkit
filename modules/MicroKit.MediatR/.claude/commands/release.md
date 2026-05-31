# /release

Prepare and execute a release of the 4 MicroKit.MediatR packages.

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
2. Agent performs pre-flight checks (branch, CI status, changelog, dependency-guardian)
3. Agent validates build and tests in Release configuration
4. Agent packs all 4 packages
5. Agent validates the package dependency graph
   (FluentValidation/Polly only in Behaviors, NSubstitute only in Testing, no FluentAssertions)
6. Present summary for approval before tagging
7. On approval: agent creates and pushes the tag
8. Tag push triggers GitHub Actions release.yml → NuGet publish
```

## Packages (4)

- `MicroKit.MediatR.Abstractions`
- `MicroKit.MediatR`
- `MicroKit.MediatR.Behaviors`
- `MicroKit.MediatR.Testing`

## Version Resolution

If `--version` is not provided, the version is computed from `Nerdbank.GitVersioning` based on
`version.json` and the current commit height. `--pre` adds a pre-release suffix: `1.2.0-beta.1`.

## Abort Conditions

The command will **not proceed** if:
- Any test is failing
- `CHANGELOG.md` has no entry for the target version
- Not on `main` or `release/mediatr-*` branch
- `dependency-guardian` check fails
- `MicroKit.MediatR.Abstractions` is being version-bumped without `api-reviewer` approval

## Tag Convention

```
mediatr-v1.0.0
mediatr-v1.1.0-beta.1
```
