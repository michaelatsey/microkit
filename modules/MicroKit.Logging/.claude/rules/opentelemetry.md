# Rule: OpenTelemetry

Rules for `MicroKit.Logging.OpenTelemetry` — the OTEL bridge.

## Fundamental Constraint

`MicroKit.Logging.OpenTelemetry` is an **opt-in bridge**. It must be possible to use all of MicroKit.Logging (correlation, enrichment, context propagation) without ever adding this package.

## Dependencies

- `MicroKit.Logging.OpenTelemetry` may depend on `MicroKit.Logging.Abstractions` and `MicroKit.Logging` core
- No other MicroKit module may depend on `MicroKit.Logging.OpenTelemetry`
- OpenTelemetry SDK packages (`OpenTelemetry`, `OpenTelemetry.Logs`, `OpenTelemetry.Trace`) are confined to this project

## Signals

### Logs
- Bridge `ILogger` → OpenTelemetry Logs export via `OpenTelemetryLoggerProvider`
- Do not duplicate `TraceId`/`SpanId` — these come from the active `Activity`, not from MicroKit context
- Enrich with canonical property names from `LogPropertyNames`

### Traces
- Bridge `ActivitySource` events → OTEL Traces via `AddSource()`
- Do not create new `Activity` objects in the bridge — consume existing ones

### Metrics (future)
- Not in scope for v1. Reserve the namespace `MicroKit.Logging.OpenTelemetry.Metrics` for future use.

## W3C TraceContext

- Inbound: extract `traceparent` and `tracestate` headers to resume a parent `Activity`
- Outbound: inject `traceparent` header from `Activity.Current`
- Use `System.Diagnostics.Activity` APIs — never implement header parsing manually

## Registration Pattern

```csharp
// ✅ Correct — consumer opt-in
services.AddLogging(builder =>
{
    builder.AddMicroKitLogging();          // Core, no OTEL
    builder.AddMicroKitOpenTelemetry();    // OTEL bridge, optional
});
```

The bridge must not be auto-registered by any other MicroKit package.

## Sampling

- Never call `ActivitySource.StartActivity()` without checking `HasListeners()`
- Respect the OTEL sampler decision — do not create `Activity` objects when sampling says no
