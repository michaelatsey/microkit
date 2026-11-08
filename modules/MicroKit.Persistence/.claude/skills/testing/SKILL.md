---
name: testing
description: How to run, filter, and interpret tests for MicroKit.Persistence. Use whenever you need to run unit, integration (SQLite, PostgreSQL via Testcontainers), architecture, or analyzer tests, filter by name, collect coverage, or diagnose a failing test. xUnit + Shouldly + NSubstitute; FluentAssertions is forbidden.
---

# Skill: Testing

Conventions and what to cover: `.claude/rules/testing.md`.

## Run

```bash
# Everything (build first — Release)
dotnet test modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release

# One project
dotnet test modules/MicroKit.Persistence/tests/MicroKit.Persistence.UnitTests/ -c Release
dotnet test modules/MicroKit.Persistence/tests/MicroKit.Persistence.IntegrationTests/ -c Release
dotnet test modules/MicroKit.Persistence/tests/MicroKit.Persistence.ArchitectureTests/ -c Release
dotnet test modules/MicroKit.Persistence/tests/MicroKit.Persistence.Analyzers.Tests/ -c Release
```

The PostgreSQL suite (`IntegrationTests/PostgreSql/`) needs Docker; without it the
`[DockerRequiredFact]`/`[DockerRequiredTheory]` tests are reported as skipped, not passed. Check
the skipped count before trusting a green run that touches transactions.

## Filter

No test carries a `Category` trait; filter by name:

```bash
dotnet test … --filter "FullyQualifiedName~MicroKit.Persistence.IntegrationTests.PostgreSql"
dotnet test … --filter "FullyQualifiedName~EfRepositoryAggregateLoadTests"
dotnet test … --filter "Name~FindAsync"
```

## Coverage

```bash
dotnet test modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release \
  --collect:"XPlat Code Coverage" --results-directory coverage/
```

## Interpreting failures

| Failure | Likely cause | First action |
|---------|--------------|--------------|
| `LayerDependencyTests` | A forbidden reference (EF Core in Abstractions/core, sibling reference, MediatR) | Check the recent `.csproj` change |
| `ContractPlacementTests` | A contract moved to the wrong project | See `.claude/rules/abstractions.md` |
| `SealedClassTests` | Sealing changed (`EfRepository`/`EfReadRepository` must stay unsealed) | Restore it |
| `IlCallScanTests` | Library code calls the parameterless `IgnoreQueryFilters()` | Use the named overload or remove it |
| `MKP00x` in Analyzers.Tests | Detection changed, or a stub no longer matches a metadata name | `.claude/rules/analyzers.md` |
| PostgreSQL test flaky or failing | Container start, or transaction semantics regressed | Re-run once; then read ADR-006 |
