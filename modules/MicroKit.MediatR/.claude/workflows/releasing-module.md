# Workflow: Releasing a Module

End-to-end release process for the 4 MicroKit.MediatR packages.

## Prerequisites

- [ ] On `main` or `release/mediatr-*` branch
- [ ] All CI checks green
- [ ] `CHANGELOG.md` entry complete for this version
- [ ] No uncommitted changes

## Steps

### 1. Run the Release Command

```
/release
```

The `release-manager` agent handles every step from here. Review each step's output before approving.

### 2. Review Package List

Verify all 4 packages are present:
- `MicroKit.MediatR.Abstractions`
- `MicroKit.MediatR`
- `MicroKit.MediatR.Behaviors`
- `MicroKit.MediatR.Testing`

### 3. Verify the Dependency Graph

- `Abstractions` declares only `MediatR.Contracts`, `MicroKit.Domain.Abstractions`, `MicroKit.Logging.Abstractions`, `MicroKit.Result`
- `Behaviors` declares `FluentValidation` + `Polly`
- `Testing` declares `NSubstitute`
- No `FluentAssertions` anywhere

### 4. Approve Tag Creation

The agent presents the tag (`mediatr-v{version}`) and asks for confirmation before pushing.

### 5. Verify GitHub Actions

```bash
gh run list --workflow=release.yml --limit=3
```

The `release.yml` workflow publishes all 4 packages to NuGet.org on tag push.

### 6. Post-Release

- [ ] GitHub Release created with changelog excerpt
- [ ] `CHANGELOG.md` updated with release date
- [ ] Samples updated if the API changed

## Rollback

```bash
# NuGet packages cannot be deleted — only unlisted
dotnet nuget delete MicroKit.MediatR.Abstractions {version} --source nuget.org
```

Then create a patch release (`mediatr-v{major}.{minor}.{patch+1}`) with the fix.
