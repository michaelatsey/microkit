# Standard: Diagnostics Events

**Canonical `DiagnosticSource` event names and payload shapes for MicroKit.Logging.**

This is the authoritative catalog. Before emitting a new event, add it here first.

## Convention

Format: `MicroKit.{Module}.{Operation}.{State}`

- **Module**: PascalCase module name (`Logging`, `MediatR`, `Persistence`)
- **Operation**: the logical operation (`Enrichment`, `Scope`, `Correlation`)
- **State**: terminal state (`Executed`, `Faulted`, `Created`, `Disposed`, `Resolved`, `Failed`)

---

## MicroKit.Logging Events

### Enrichment Pipeline

| Event Name | When Emitted | Payload |
|-----------|-------------|---------|
| `MicroKit.Logging.Enrichment.Executed` | After pipeline completes successfully | `{ int EnricherCount, string OperationId, double ElapsedMs }` |
| `MicroKit.Logging.Enrichment.Faulted` | When an enricher throws | `{ string EnricherType, Exception Exception, string OperationId }` |
| `MicroKit.Logging.Enrichment.Skipped` | When log level is not active | `{ LogLevel Level, string Category }` |

### Scope Lifecycle

| Event Name | When Emitted | Payload |
|-----------|-------------|---------|
| `MicroKit.Logging.Scope.Created` | When a new operation scope is opened | `{ string ScopeName, string OperationId, string CorrelationId }` |
| `MicroKit.Logging.Scope.Disposed` | When an operation scope is closed | `{ string ScopeName, string OperationId, double DurationMs }` |

### Correlation

| Event Name | When Emitted | Payload |
|-----------|-------------|---------|
| `MicroKit.Logging.Correlation.Resolved` | When CorrelationId is extracted from inbound context | `{ string CorrelationId, string Source }` |
| `MicroKit.Logging.Correlation.Generated` | When a new CorrelationId is generated | `{ string CorrelationId }` |

---

## Reserved — Other MicroKit Modules

These names are reserved. They will be implemented in their respective modules.

| Event Name | Module |
|-----------|--------|
| `MicroKit.MediatR.Command.Started` | MicroKit.MediatR |
| `MicroKit.MediatR.Command.Completed` | MicroKit.MediatR |
| `MicroKit.MediatR.Command.Faulted` | MicroKit.MediatR |
| `MicroKit.Messaging.Outbox.Published` | MicroKit.Messaging |
| `MicroKit.Messaging.Outbox.Delivered` | MicroKit.Messaging |
| `MicroKit.Messaging.Outbox.DeliveryFailed` | MicroKit.Messaging |
| `MicroKit.MultiTenancy.Tenant.Resolved` | MicroKit.MultiTenancy |
| `MicroKit.MultiTenancy.Tenant.ResolutionFailed` | MicroKit.MultiTenancy |
| `MicroKit.Persistence.Transaction.Started` | MicroKit.Persistence |
| `MicroKit.Persistence.Transaction.Committed` | MicroKit.Persistence |
| `MicroKit.Persistence.Transaction.RolledBack` | MicroKit.Persistence |

---

## Implementation Pattern

```csharp
// Always guard before payload construction
if (_diagnosticListener.IsEnabled(DiagnosticEventNames.EnrichmentExecuted))
{
    // Document payload shape above the Write() call
    // Shape: { int EnricherCount, string OperationId, double ElapsedMs }
    _diagnosticListener.Write(DiagnosticEventNames.EnrichmentExecuted, new
    {
        EnricherCount = count,
        OperationId = context.OperationId,
        ElapsedMs = elapsed.TotalMilliseconds
    });
}
```

Constants for all event names live in `DiagnosticEventNames` in `MicroKit.Logging.Diagnostics`.
