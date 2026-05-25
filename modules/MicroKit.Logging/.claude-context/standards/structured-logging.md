# Standard: Structured Logging

**Canonical rules for structured log messages in MicroKit.Logging and consuming code.**

---

## Message Template Syntax

MicroKit.Logging follows the [Message Templates](https://messagetemplates.org/) specification.

```csharp
// ✅ Named placeholders — structured, queryable
logger.LogInformation("Processing {CommandName} for tenant {TenantId}", commandName, tenantId);

// ❌ Interpolated string — allocates, not queryable
logger.LogInformation($"Processing {commandName} for tenant {tenantId}");

// ❌ Concatenation — allocates, not queryable
logger.LogInformation("Processing " + commandName + " for tenant " + tenantId);
```

## Named Placeholder Rules

1. **PascalCase** — `{CommandName}`, `{TenantId}`, not `{commandName}`, `{tenant_id}`
2. **Use canonical names** — placeholders that match `LogPropertyNames.*` are automatically indexed by backends
3. **Destructuring with `@`** — use `{@Object}` for complex objects you want destructured, not serialized as `.ToString()`
4. **No sensitive data** — never include passwords, tokens, or PII in message templates

## LoggerMessage Pattern (mandatory on hot paths)

```csharp
// ✅ Source generator — zero allocation
internal static partial class EnrichmentPipelineLog
{
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Debug,
        Message = "Enrichment pipeline completed {EnricherCount} enrichers in {ElapsedMs}ms for operation {OperationId}")]
    public static partial void PipelineCompleted(
        this ILogger logger,
        int enricherCount,
        double elapsedMs,
        string operationId);
}

// Usage
_logger.PipelineCompleted(enricherCount, elapsed.TotalMilliseconds, operationId);
```

## Log Level Semantics

| Level | When to use in MicroKit |
|-------|------------------------|
| `Trace` | Internal loop iterations, per-property enrichment steps — very high volume |
| `Debug` | Framework lifecycle events — pipeline start/stop, scope open/close |
| `Information` | Significant state changes — OTEL bridge attached, first enricher registered |
| `Warning` | Recoverable unexpected conditions — enricher faulted, correlation ID missing |
| `Error` | Framework failure requiring attention — configuration error, pipeline broken |
| `Critical` | Never used in library code — reserved for application-level failures |

## Scope Enrichment Pattern

```csharp
// ✅ Dictionary scope with canonical constants
using (_logger.BeginScope(new Dictionary<string, object?>
{
    [LogPropertyNames.OperationId] = operationId,
    [LogPropertyNames.CorrelationId] = correlationId
}))
{
    // All logs within this block carry OperationId and CorrelationId
}
```
