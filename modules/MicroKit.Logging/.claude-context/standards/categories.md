# Standard: Log Categories

**Canonical `ILogger` category names for MicroKit modules.**

The category is the first argument to `ILogger<T>` — it determines log routing, filtering, and querying in backends.

## Convention

Format: `MicroKit.{Module}[.{SubComponent}]`

All categories are defined as constants in `LogCategoryNames` in `MicroKit.Logging.Abstractions`.

---

## Category Registry

### MicroKit.Logging

| Constant | String Value | Component |
|----------|-------------|-----------|
| `LogCategoryNames.EnrichmentPipeline` | `"MicroKit.Logging.EnrichmentPipeline"` | Core enrichment pipeline |
| `LogCategoryNames.ContextPropagation` | `"MicroKit.Logging.ContextPropagation"` | AsyncLocal / Activity context |
| `LogCategoryNames.ScopeManagement` | `"MicroKit.Logging.ScopeManagement"` | Log scope lifecycle |
| `LogCategoryNames.OpenTelemetry` | `"MicroKit.Logging.OpenTelemetry"` | OTEL bridge |
| `LogCategoryNames.AspNetCore` | `"MicroKit.Logging.AspNetCore"` | HTTP middleware |
| `LogCategoryNames.Diagnostics` | `"MicroKit.Logging.Diagnostics"` | ActivitySource / DiagnosticSource |

### Other MicroKit Modules (reserved)

| Category Prefix | Module |
|----------------|--------|
| `MicroKit.MediatR.*` | MicroKit.MediatR |
| `MicroKit.Persistence.*` | MicroKit.Persistence |
| `MicroKit.Messaging.*` | MicroKit.Messaging |
| `MicroKit.Auth.*` | MicroKit.Auth |
| `MicroKit.MultiTenancy.*` | MicroKit.MultiTenancy |

---

## Filtering Configuration Example

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "MicroKit": "Warning",
      "MicroKit.Logging.EnrichmentPipeline": "Debug",
      "MicroKit.Logging.OpenTelemetry": "Information"
    }
  }
}
```

This allows fine-grained control per MicroKit subsystem without exposing internal implementation type names.
