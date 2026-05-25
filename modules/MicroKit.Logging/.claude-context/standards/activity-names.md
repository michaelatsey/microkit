# Standard: Activity Names

**Canonical `ActivitySource` names and `Activity` operation names for MicroKit.**

All `ActivitySource` instances and `Activity` operation names must use these values. Never invent ad-hoc names.

## Convention

- **Source name**: `MicroKit.{Module}` (identifies the component)
- **Operation name**: `{Component}.{Operation}` (describes what's happening)
- **Tag keys**: always use `LogPropertyNames.*` constants

---

## ActivitySource Registry

| Constant | Source Name | Version | Owner |
|----------|------------|---------|-------|
| `ActivitySources.Logging` | `"MicroKit.Logging"` | Assembly version | MicroKit.Logging.Diagnostics |
| `ActivitySources.Enrichment` | `"MicroKit.Logging.Enrichment"` | Assembly version | MicroKit.Logging |

## Activity Operation Names

### MicroKit.Logging

| Operation Name | Description | Key Tags |
|---------------|-------------|---------|
| `EnrichmentPipeline.Execute` | Full enrichment pipeline execution | `OperationId`, `EnricherCount` |
| `OperationScope.Begin` | Opening a new operation scope | `OperationId`, `CorrelationId` |
| `Correlation.Resolve` | Resolving CorrelationId from context | `CorrelationId`, `Source` |

### Reserved for Other MicroKit Modules

| Operation Name | Module |
|---------------|--------|
| `Command.Execute` | MicroKit.MediatR |
| `Query.Execute` | MicroKit.MediatR |
| `Outbox.Publish` | MicroKit.Messaging |
| `Outbox.Deliver` | MicroKit.Messaging |
| `Tenant.Resolve` | MicroKit.MultiTenancy |
| `Auth.Validate` | MicroKit.Auth |

---

## OTEL Instrumentation Scope

When registering sources with OTEL:

```csharp
builder.AddSource("MicroKit.*")  // captures all MicroKit modules
// or individual:
builder.AddSource("MicroKit.Logging")
builder.AddSource("MicroKit.Logging.Enrichment")
```

## Tag Naming

Activity tags must use `LogPropertyNames.*` constants for all canonical properties:

```csharp
activity.SetTag(LogPropertyNames.OperationId, operationId);
activity.SetTag(LogPropertyNames.TenantId, tenantId);
activity.SetTag(LogPropertyNames.CorrelationId, correlationId);
```
