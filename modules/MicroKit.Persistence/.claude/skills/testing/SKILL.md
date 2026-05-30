---
name: testing
description: How to run, filter, and interpret tests for MicroKit.Persistence. Use whenever you need to run unit/integration/architecture/performance tests, filter by name, collect coverage, or diagnose a failing test (analyzer violation, missing AsNoTracking, N+1 detection). Enforces Shouldly + NSubstitute; FluentAssertions is banned.
---

# Skill: Testing

How to run, filter, and interpret tests for MicroKit.Persistence.

## Stack (non-negotiable)

- **xUnit** framework, **Shouldly** assertions, **NSubstitute** mocks
- **FluentAssertions is banned** (commercial license). Never `.Should().`
- Repository isolation via `InMemoryRepository<T>` from `MicroKit.Persistence.Testing`
- Integration tests via SQLite in-memory or Testcontainers

## Run All Tests

```bash
dotnet test modules/MicroKit.Persistence/MicroKit.Persistence.slnx --no-build -c Release
```

## Run by Category

```bash
# Unit tests (fast — no DB)
dotnet test modules/MicroKit.Persistence/tests/MicroKit.Persistence.UnitTests/ --no-build

# Architecture tests (dependency rules, no EF in Abstractions)
dotnet test modules/MicroKit.Persistence/tests/MicroKit.Persistence.ArchitectureTests/ --no-build

# Integration tests (EF Core, SQLite in-memory)
dotnet test modules/MicroKit.Persistence/tests/MicroKit.Persistence.IntegrationTests/ \
  --no-build --filter "Category=SQLite"

# Performance tests (BenchmarkDotNet — run separately, slow)
dotnet test modules/MicroKit.Persistence/tests/MicroKit.Persistence.PerformanceTests/ --no-build
```

## Filter by Test Name

```bash
dotnet test --filter "ClassName=UserRepositoryTests"
dotnet test --filter "Name~FindAsync"
dotnet test --filter "FullyQualifiedName~MicroKit.Persistence.UnitTests"
```

## Code Coverage

```bash
dotnet test modules/MicroKit.Persistence/MicroKit.Persistence.slnx \
  --collect:"XPlat Code Coverage" --results-directory coverage/

reportgenerator -reports:"coverage/**/*.xml" -targetdir:"coverage/report" -reporttypes:Html
```

## Interpreting Failures

| Failure type | Likely cause | First action |
|-------------|-------------|-------------|
| `ArchitectureTests` fail | EF type in Abstractions, or mutation in IReadRepository | Check recent `.csproj` changes |
| `PRDANA001` build error | DbContext injected into a handler | Replace with typed repository |
| `PRDANA002` build error | SaveChanges in read repo | Remove the call |
| `.Should()` compile error | FluentAssertions slipped in | Replace with Shouldly |
| Integration test fails | DB schema mismatch | Check migration is up to date |

## Test Project Configuration

```xml
<GenerateDocumentationFile>false</GenerateDocumentationFile>
<NoWarn>CS1591;CA1707</NoWarn>
```

See `.claude/rules/testing.md` for the full convention and the mandatory case matrix.
