# /logging-new-provider

Scaffold a new logging provider integration project for MicroKit.Logging.

## Usage

```
/logging-new-provider <ProviderName>
```

**Examples:**
```
/logging-new-provider Datadog
/logging-new-provider NLog
/logging-new-provider ApplicationInsights
```

## What This Command Does

1. Creates `src/MicroKit.Logging.<ProviderName>/` with the standard project structure
2. Generates the `.csproj` with correct project references (no inline versions)
3. Creates the DI registration extension class
4. Creates the enricher bridge class skeleton
5. Creates `tests/MicroKit.Logging.<ProviderName>.IntegrationTests/` skeleton
6. Adds both projects to `MicroKit.Logging.slnx`
7. Runs `logging-dependency-guardian` agent to validate references

## Scaffold Template

Load `.claude-context/templates/logging-provider-template.md` for the exact code structure.

## Steps

```
1. Confirm provider name follows PascalCase (no dots, no hyphens)
2. Verify MicroKit.Logging.slnx exists
3. Create src/MicroKit.Logging.{ProviderName}/ directory
4. Scaffold .csproj — reference template, NO version attributes
5. Scaffold LoggingBuilderExtensions.cs (DI entry point)
6. Scaffold {ProviderName}LogEnricher.cs implementing ILogEnricher
7. Add XML docs on all public members
8. Create tests/ skeleton with one smoke test
9. Add both projects to .slnx
10. Run: Use agent logging-dependency-guardian to validate
```

## Constraints

- The new project may reference `MicroKit.Logging.Abstractions` and `MicroKit.Logging` core only — no cross-provider references
- The new project name must be `MicroKit.Logging.<ProviderName>` exactly
- DI extension method must be on `ILoggingBuilder`, not on `IServiceCollection` directly
- All `PackageReference` versions go into `build/Directory.Packages.props`
