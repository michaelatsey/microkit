# /logging-generate-tests

Generate a complete xUnit test suite for a target class or interface.

## Usage

```
/logging-generate-tests <TargetClass> [--project <TestProjectName>]
```

**Examples:**
```
/logging-generate-tests EnrichmentPipeline
/logging-generate-tests TenantLogEnricher --project UnitTests
/logging-generate-tests CorrelationMiddleware --project IntegrationTests
```

## Steps

```
1. Load .claude/rules/logging-testing.md
2. Load .claude-context/templates/logging-test-template.md
3. Read the target class source file
4. Identify: public methods, edge cases, async paths, exception paths
5. Generate test class:
   - File: {TargetClass}Tests.cs in correct test project
   - Class: sealed (test classes are sealed)
   - Uses xUnit + FluentAssertions + NSubstitute
   - One method per scenario, descriptive name: Method_Scenario_ExpectedResult
   - Async tests use ValueTask where the SUT is ValueTask
6. For enrichers: always include canonical property name assertions
7. For async paths: test CancellationToken propagation
8. Do NOT generate: documentation XML (GenerateDocumentationFile=false in test projects)
```

## Constraints

- `[Fact]` for deterministic tests, `[Theory]` + `[InlineData]` for parameterized
- `NSubstitute` for mocking — no Moq, no manual fakes
- `FluentAssertions` for all assertions — no `Assert.Equal`
- No `Thread.Sleep` — use `Task.Delay` with `CancellationToken` if timing needed
