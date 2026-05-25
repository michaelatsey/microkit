# Rule: Testing

## Test Project Responsibilities

| Project | Tests |
|---------|-------|
| `UnitTests` | Isolated logic, enrichers, pipeline, no I/O |
| `IntegrationTests` | OTEL correlation, middleware, real ILogger pipeline |
| `ArchitectureTests` | Dependency rules via NetArchTest |
| `PerformanceTests` | Allocation regression via BenchmarkDotNet |

## xUnit Conventions

- Test classes: `sealed` — no inheritance in test classes
- Test method naming: `Method_Scenario_ExpectedResult` — `Enrich_WhenTenantIdAvailable_AddsCanonicalProperty`
- `[Fact]` for deterministic tests, `[Theory]` + `[InlineData]` for parameterized
- `[Collection]` for tests sharing expensive fixtures (OTEL, host setup)

## Library Choices

- **xUnit** — test framework (no NUnit, no MSTest)
- **FluentAssertions** — all assertions (`result.Should().Be(...)` not `Assert.Equal(...)`)
- **NSubstitute** — all mocks and stubs (no Moq)
- **NetArchTest.Rules** — architecture tests only

## Test Project .csproj

```xml
<GenerateDocumentationFile>false</GenerateDocumentationFile>
<NoWarn>CS1591;CA1707</NoWarn>
```

These two properties are mandatory in every test project — no XML docs required, method naming convention different from production code.

## What to Test in Enrichers

Every `ILogEnricher` implementation must have tests for:
1. Correct canonical property name used (compare against `LogPropertyNames.*`)
2. Property set when context is available
3. No property set (no exception) when context is null/empty
4. No allocation on the "nothing to enrich" path (BenchmarkDotNet)
