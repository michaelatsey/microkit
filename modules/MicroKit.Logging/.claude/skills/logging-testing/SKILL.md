# Skill: Logging Testing

How to run, filter, and interpret tests for MicroKit.Logging.

## Run All Tests

```bash
dotnet test modules/MicroKit.Logging/MicroKit.Logging.slnx --no-build -c Release
```

## Run by Category

```bash
# Unit tests only (fast — use during development)
dotnet test modules/MicroKit.Logging/tests/MicroKit.Logging.UnitTests/ --no-build

# Architecture tests (dependency rules)
dotnet test modules/MicroKit.Logging/tests/MicroKit.Logging.ArchitectureTests/ --no-build

# Integration tests (requires no external services for basic correlation tests)
dotnet test modules/MicroKit.Logging/tests/MicroKit.Logging.IntegrationTests/ --no-build

# Performance tests (separate — never run on CI standard pipeline)
dotnet test modules/MicroKit.Logging/tests/MicroKit.Logging.PerformanceTests/ --no-build
```

## Filter by Test Name

```bash
# Run a specific test class
dotnet test --filter "ClassName=TenantLogEnricherTests"

# Run tests matching a pattern
dotnet test --filter "Name~Enrich_When"

# Run tests for a specific enricher
dotnet test --filter "FullyQualifiedName~MicroKit.Logging.UnitTests.Enrichers"
```

## Code Coverage

```bash
dotnet test modules/MicroKit.Logging/MicroKit.Logging.slnx \
  --collect:"XPlat Code Coverage" \
  --results-directory coverage/

# Generate HTML report (requires reportgenerator tool)
reportgenerator -reports:"coverage/**/*.xml" -targetdir:"coverage/report" -reporttypes:Html
```

## Interpreting Failures

| Failure type | Likely cause | First action |
|-------------|-------------|-------------|
| `ArchitectureTests` fail | Forbidden dependency added | Check recent `.csproj` changes |
| Enricher test fails on property name | Non-canonical name used | Check `LogPropertyNames.*` constants |
| Integration test fails on correlation | `AsyncLocal` not propagating | Check `ConfigureAwait(false)` usage |
| Performance test fails | Allocation regression | Run benchmarks, compare with baseline |

## Test Project Configuration

Test projects must have in their `.csproj`:

```xml
<GenerateDocumentationFile>false</GenerateDocumentationFile>
<NoWarn>CS1591;CA1707</NoWarn>
```
