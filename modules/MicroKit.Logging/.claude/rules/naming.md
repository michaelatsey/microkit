# Rule: Naming

## Log Property Names

Use ONLY constants from `LogPropertyNames` in `MicroKit.Logging.Abstractions`.

| Constant | Value | Description |
|----------|-------|-------------|
| `LogPropertyNames.CorrelationId` | `"CorrelationId"` | Cross-boundary correlation |
| `LogPropertyNames.TraceId` | `"TraceId"` | W3C trace ID |
| `LogPropertyNames.SpanId` | `"SpanId"` | W3C span ID |
| `LogPropertyNames.TenantId` | `"TenantId"` | From MicroKit.MultiTenancy |
| `LogPropertyNames.UserId` | `"UserId"` | From MicroKit.Auth |
| `LogPropertyNames.RequestId` | `"RequestId"` | HTTP request or message ID |
| `LogPropertyNames.OperationId` | `"OperationId"` | Business operation scope |
| `LogPropertyNames.CommandName` | `"CommandName"` | From MicroKit.MediatR |
| `LogPropertyNames.MessageId` | `"MessageId"` | From MicroKit.Messaging |

Adding a new canonical property requires updating `LogPropertyNames` in Abstractions + entry in `.claude-context/standards/log-properties.md`.

## Type Naming

| Pattern | Convention | Example |
|---------|-----------|---------|
| Interfaces | `I[Noun]` | `ILogEnricher`, `IOperationContext` |
| Enrichers | `[Noun]LogEnricher` | `TenantLogEnricher`, `HttpRequestLogEnricher` |
| Extensions | `[Type]Extensions` | `LoggingBuilderExtensions`, `LoggerExtensions` |
| Options | `[Feature]Options` | `MicroKitLoggingOptions`, `EnrichmentOptions` |
| Constants | `[Domain]Names` | `LogPropertyNames`, `LogScopeNames` |

## Method Naming

- Extension methods on `ILoggingBuilder`: `AddMicroKit[Feature]()` — `AddMicroKitLogging()`, `AddMicroKitOpenTelemetry()`
- Enrichment registration: `WithEnricher<T>()` or `AddEnricher<T>()`
- Scope creation: `BeginOperationScope()`, `BeginTenantScope()` — not `CreateScope()`

## File Naming

- One type per file
- Filename matches type name: `TenantLogEnricher.cs`, `ILogEnricher.cs`
- Partial classes: `{TypeName}.{Concern}.cs` — `EnrichmentPipeline.Registration.cs`
