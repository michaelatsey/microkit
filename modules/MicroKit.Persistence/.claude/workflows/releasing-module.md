# Workflow: Releasing the Module

End-to-end release of the 8 MicroKit.Persistence packages. The root `/release Persistence`
command runs the `release-manager` agent through these steps. ADR-GLOBAL-002 (root) governs; no
tag is cut until steps 1–4 of its migration land.

## Prerequisites

- [ ] On `main`, clean tree, CI green
- [ ] `CHANGELOG.md` complete for the version
- [ ] ArchitectureTests present and green

## Steps

### 1. Build and test in Release, with Docker

```bash
dotnet build modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release
dotnet test  modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release --no-build
```

No PostgreSQL test may be skipped.

### 2. Pack and smoke-test

```bash
dotnet pack modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release \
  -p:PackageVersion={version} -o artifacts/persistence/
```

- 8 packages present
- Declared dependencies match `.claude/rules/packaging.md`; Analyzers has no `lib/`
- Consumed from a local folder feed by a real consumer before any NuGet.org publish

### 3. Tag — after the human confirms

```bash
git tag persistence-v{version} -m "MicroKit.Persistence {version}"
git push origin persistence-v{version}
```

### 4. Watch the workflow

```bash
gh run list --workflow=release-persistence.yml --limit=3
```

### 5. Post-release

- [ ] GitHub Release with the CHANGELOG excerpt
- [ ] `MicroKit.MediatR` and `MicroKit.Tenancy` told when a contract they use changed
      (`ITransactionalContext`, `IUnitOfWork`, `EfRepository`, conventions)

## Breaking changes

On `preview.*` a known break ships outright, without `[Obsolete]` (ADR-GLOBAL-002 D10):
`!` in the commit header (`refactor(persistence)!:`), a `BREAKING CHANGE:` footer with the
migration, a CHANGELOG entry, and an ADR in `.claude-context/context/architectural-decisions.md`
when the decision outlives the change.

## Rollback

NuGet packages cannot be deleted, only unlisted. Ship the fix as the next version.
