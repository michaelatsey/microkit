# /logging-new-enricher

Scaffold a new `ILogEnricher` implementation within MicroKit.Logging or a provider project.

## Usage

```
/logging-new-enricher <EnricherName> [--project <ProjectName>]
```

**Examples:**
```
/logging-new-enricher Tenant
/logging-new-enricher HttpRequest --project AspNetCore
/logging-new-enricher CqrsOperation --project Core
```

If `--project` is omitted, defaults to `MicroKit.Logging` core.

## What This Command Does

1. Creates `<EnricherName>LogEnricher.cs` in the target project
2. Creates the corresponding unit test file
3. Registers the enricher in the project's DI extension (if applicable)
4. Validates property names against `.claude-context/standards/log-properties.md`

## Steps

```
1. Load .claude-context/standards/log-properties.md
2. Load .claude-context/templates/logging-enricher-template.md
3. Determine target project: MicroKit.Logging.{Project} or MicroKit.Logging (core)
4. Verify ILogEnricher contract in MicroKit.Logging.Abstractions
5. Scaffold {EnricherName}LogEnricher.cs — sealed, implements ILogEnricher
6. Ensure enrichment properties use ONLY canonical names from log-properties.md
7. Add lazy evaluation guard: check ILogger.IsEnabled before property computation
8. Scaffold {EnricherName}LogEnricherTests.cs with:
   - Enriches_WhenContextIsAvailable
   - DoesNotEnrich_WhenContextIsNull
   - UsesCanonicalPropertyNames
9. Register in DI extension method of target project
```

## Constraints

- **`sealed`** — all enricher classes are sealed
- **Zero allocation** on hot path — no LINQ, no closures, no string interpolation
- **Lazy evaluation** — never compute a property value if the log level is not active
- **Canonical names only** — any property not in `log-properties.md` is blocked; add to standards first
- **No `ILogger` injection** in enrichers — enrichers receive context, they do not log
