---

name: microkit-multitenancy-testing
description: Execute, validate, and troubleshoot tests for the MicroKit.Multitenancy solution, including unit, integration, and architecture test suites.
---------------------------------------------------------------------------------------------------------------------------------------------------------

# Purpose

Run and validate the MicroKit.Multitenancy test suites while ensuring compliance with repository testing standards, architecture rules, and coverage requirements.

## When to Use

Use this skill when:

* Running all tests for MicroKit.Multitenancy.
* Running a specific test suite.
* Executing targeted tests during development.
* Investigating failing tests.
* Collecting code coverage metrics.
* Validating architecture constraints.
* Verifying compliance with testing conventions.
* Checking for prohibited testing libraries.

## When NOT to Use

Do not use this skill when:

* Building projects without executing tests.
* Implementing new functionality.
* Performing architectural design work.
* Managing package dependencies.
* Troubleshooting compilation errors unrelated to tests.

# Instructions

## Run the Complete Test Suite

Execute all tests using the Release configuration.

```bash
dotnet test modules/MicroKit.Multitenancy/MicroKit.Multitenancy.slnx -c Release
```

Use this command before merging significant changes or validating a feature branch.

## Run Individual Test Suites

### Unit Tests

```bash
dotnet test modules/MicroKit.Multitenancy/tests/MicroKit.Multitenancy.UnitTests -c Release
```

Use when validating isolated business logic and domain behavior.

### Integration Tests

```bash
dotnet test modules/MicroKit.Multitenancy/tests/MicroKit.Multitenancy.IntegrationTests -c Release
```

Use when validating framework integrations, persistence behavior, or cross-component interactions.

### Architecture Tests

```bash
dotnet test modules/MicroKit.Multitenancy/tests/MicroKit.Multitenancy.ArchitectureTests -c Release
```

Use when validating dependency rules, layering constraints, and architectural boundaries.

## Run Targeted Tests

Use test filters to execute a subset of tests.

Example:

```bash
dotnet test --filter "DisplayName~AsyncLocal" -c Release
```

Example:

```bash
dotnet test --filter "DisplayName~TenantIsolation" -c Release
```

Use targeted execution during development or when investigating regressions.

## Collect Code Coverage

Generate coverage reports using the XPlat collector.

```bash
dotnet test modules/MicroKit.Multitenancy/MicroKit.Multitenancy.slnx \
  --collect:"XPlat Code Coverage" \
  --results-directory coverage/
```

After execution:

* Verify coverage artifacts are generated.
* Review uncovered code paths.
* Ensure critical multitenancy scenarios are covered.

## Verify Testing Standards

FluentAssertions is prohibited within this repository.

Check for violations:

```bash
grep -rn 'FluentAssertions\|\.Should()\.' modules/MicroKit.Multitenancy/tests/ --include="*.cs"
```

Expected result:

```text
(no output)
```

If matches are found:

1. Remove FluentAssertions usage.
2. Replace assertions with approved assertion mechanisms.
3. Re-run the affected test suite.

# Test Execution Strategy

Follow this order when validating changes:

1. Run targeted tests related to the modified code.
2. Run the corresponding test suite.
3. Run architecture tests.
4. Run the complete solution test suite.
5. Generate coverage when preparing significant changes or releases.

# Troubleshooting

## Test Discovery Failure

Possible causes:

* Incorrect project path.
* Build failure before test execution.
* Missing package restore.

Resolution:

1. Restore dependencies.
2. Build the solution.
3. Re-run test discovery.

## Architecture Test Failure

Possible causes:

* Invalid project dependency.
* Layering violation.
* Forbidden reference between assemblies.

Resolution:

1. Identify the failing architectural rule.
2. Remove the dependency violation.
3. Re-run architecture tests before continuing.

## Integration Test Failure

Possible causes:

* Configuration issue.
* Incorrect tenant isolation behavior.
* Infrastructure regression.

Resolution:

1. Reproduce using the integration suite only.
2. Isolate the failing scenario.
3. Verify tenant boundaries and data isolation rules.

# Validation Checklist

Before considering testing complete:

* All targeted tests pass.
* Unit tests pass.
* Integration tests pass.
* Architecture tests pass.
* Full solution test suite passes.
* No FluentAssertions usage exists.
* Coverage artifacts are generated when requested.
* No test failures remain unresolved.
