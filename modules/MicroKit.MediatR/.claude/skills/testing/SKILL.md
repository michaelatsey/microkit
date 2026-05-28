---
name: testing
description: How to run, filter, and interpret tests for MicroKit.MediatR. Use whenever you need to run unit/integration/architecture/performance tests, filter by name, collect coverage, or diagnose a failing test (architecture rule violation, CQRS contract failure, behavior ordering). Enforces Shouldly + NSubstitute; FluentAssertions is banned.
---

# Skill: Testing

How to run, filter, and interpret tests for MicroKit.MediatR.

## Stack (non-negotiable)

- **xUnit** framework, **Shouldly** assertions, **NSubstitute** mocks
- **FluentAssertions is banned** (commercial license). Never `.Should().`
- Handler/behavior isolation via `MicroKit.MediatR.Testing` harnesses — no real `IMediator` in unit tests

## Run All Tests

```bash
dotnet test modules/MicroKit.MediatR/MicroKit.MediatR.slnx --no-build -c Release
```

## Run by Category

```bash
# Unit tests (fast — use during development)
dotnet test modules/MicroKit.MediatR/tests/MicroKit.MediatR.UnitTests/ --no-build

# Architecture tests (CQRS + dependency rules)
dotnet test modules/MicroKit.MediatR/tests/MicroKit.MediatR.ArchitectureTests/ --no-build

# Integration tests (full pipeline via DI, behavior ordering)
dotnet test modules/MicroKit.MediatR/tests/MicroKit.MediatR.IntegrationTests/ --no-build

# Performance tests (separate — never on the standard CI pipeline)
dotnet test modules/MicroKit.MediatR/tests/MicroKit.MediatR.PerformanceTests/ --no-build
```

## Filter by Test Name

```bash
dotnet test --filter "ClassName=CreateOrderHandlerTests"
dotnet test --filter "Name~Handle_WhenUserNotFound"
dotnet test --filter "FullyQualifiedName~MicroKit.MediatR.UnitTests.Behaviors"
```

## Code Coverage

```bash
dotnet test modules/MicroKit.MediatR/MicroKit.MediatR.slnx \
  --collect:"XPlat Code Coverage" --results-directory coverage/

reportgenerator -reports:"coverage/**/*.xml" -targetdir:"coverage/report" -reporttypes:Html
```

## Interpreting Failures

| Failure type | Likely cause | First action |
|-------------|-------------|-------------|
| `ArchitectureTests` fail | Forbidden dependency or CQRS violation | Check recent `.csproj` / handler changes |
| Behavior ordering test fails | DI registration order ≠ PipelineOrder | Check `AddMicroKitMediatR` registration order |
| `.Should()` compile/lint error | FluentAssertions slipped in | Replace with Shouldly |
| Handler test needs DI/DbContext | Over-coupled handler | Refactor per `no-handler-coupling.md` |

## Test Project Configuration

```xml
<GenerateDocumentationFile>false</GenerateDocumentationFile>
<NoWarn>CS1591;CA1707</NoWarn>
```

See `.claude/rules/testing.md` for the full convention and the mandatory case matrix.
For generating tests, use `/new-handler-tests` (delegates to the `handler-test-generator` agent).
