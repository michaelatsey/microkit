# Workflow: Releasing a Module

End-to-end release process for the 8 MicroKit.Persistence packages.

## Prerequisites

- [ ] On `main` or `release/persistence-*` branch
- [ ] All CI checks green (all 3 providers tested)
- [ ] `CHANGELOG.md` entry complete for this version
- [ ] No uncommitted changes
- [ ] Migration scripts reviewed (no destructive operations without documentation)

## Steps

### 1. Run the Release Command

```
/release
```

The `release-manager` agent handles every step. Review each step's output before approving.

### 2. Review Package List (8 packages)

Verify all 8 packages are present:
- `MicroKit.Persistence.Abstractions`
- `MicroKit.Persistence`
- `MicroKit.Persistence.EntityFrameworkCore`
- `MicroKit.Persistence.EntityFrameworkCore.PostgreSql`
- `MicroKit.Persistence.EntityFrameworkCore.SqlServer`
- `MicroKit.Persistence.Specifications`
- `MicroKit.Persistence.Testing`
- `MicroKit.Persistence.Analyzers`

### 3. Verify Dependency Graph

- `Abstractions` declares only `MicroKit.Result` + `MicroKit.Domain.Abstractions`
- `Core` adds `MicroKit.Logging.Abstractions`
- `EntityFrameworkCore` adds `Microsoft.EntityFrameworkCore`
- Provider packages add only their provider NuGet
- `Testing` declares `NSubstitute`
- `Analyzers` has no `lib/` in the nupkg (build-only)
- No `FluentAssertions` anywhere

### 4. Approve Tag Creation

The agent presents the tag (`persistence-v{version}`) and asks for confirmation before pushing.

```bash
git tag persistence-v{version} -m "MicroKit.Persistence {version}"
git push origin persistence-v{version}
```

### 5. Verify GitHub Actions

```bash
gh run list --workflow=release-persistence.yml --limit=3
```

### 6. Post-Release

- [ ] GitHub Release created with changelog excerpt
- [ ] Breaking change migration guide updated if ADR-001 IUnitOfWork migration guidance applies
- [ ] Samples updated if the API changed
- [ ] Notify MicroKit.MediatR team if ITransactionalContext signature changed (cross-module impact)

## Breaking Change Protocol

If `IUnitOfWork`, `ITransactionalContext`, or `IRepository<T>` signatures change:
1. Add `!` to the commit scope: `feat(persistence)!:`
2. Add `BREAKING CHANGE:` footer with migration guide
3. Increment major version in `version.json`
4. Update ADR in `.claude-context/context/architectural-decisions.md`

## Rollback

```bash
dotnet nuget delete MicroKit.Persistence.Abstractions {version} --source nuget.org
```
Then create a patch release with the fix.
