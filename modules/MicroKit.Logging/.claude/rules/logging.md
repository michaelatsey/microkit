# Rule: Logging

Rules for how logging is used **within** MicroKit.Logging itself (the library logging its own operations), and rules for the patterns the library enforces on consumers.

## Internal Logging (library → ILogger)

MicroKit.Logging uses `ILogger<T>` internally for framework-level diagnostics.

### Mandatory

- **No `Console.WriteLine`** anywhere — use `ILogger<T>` exclusively
- **`LoggerMessage` source generator** for all internal log definitions — no ad-hoc `logger.LogInformation(...)` with string templates
- **`[LoggerMessage]` attribute** syntax preferred over `LoggerMessage.Define()` static factory
- Log at `Debug` or `Trace` for internal framework events — never `Information` for routine operations

### Example (correct)

```csharp
internal static partial class EnrichmentPipelineLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Enrichment pipeline executed {EnricherCount} enrichers for operation {OperationId}")]
    public static partial void EnrichersExecuted(this ILogger logger, int enricherCount, string operationId);
}
```

## Consumer-Facing Patterns

These are the patterns MicroKit.Logging enforces via analyzers on consuming code:

### Structured Logging

```csharp
// ✅ Correct
logger.LogInformation("Processing command {CommandName} for tenant {TenantId}", commandName, tenantId);

// ❌ Wrong — string interpolation
logger.LogInformation($"Processing command {commandName}");

// ❌ Wrong — concatenation
logger.LogInformation("Processing command " + commandName);
```

### Canonical Property Names

Always use the constants from `LogPropertyNames`:

```csharp
// ✅ Correct
using (logger.BeginScope(new Dictionary<string, object>
{
    [LogPropertyNames.TenantId] = tenantId,
    [LogPropertyNames.CorrelationId] = correlationId
}))

// ❌ Wrong
using (logger.BeginScope(new { tenant_id = tenantId, correlation = correlationId }))
```

### Log Level Guards

```csharp
// ✅ Correct — expensive computation guarded
if (logger.IsEnabled(LogLevel.Debug))
{
    logger.LogDebug("Pipeline state: {State}", ComputeExpensiveState());
}

// ❌ Wrong — always computed even when Debug is disabled
logger.LogDebug("Pipeline state: {State}", ComputeExpensiveState());
```
