# Rule: Logging OpenTelemetry

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
services.AddMicroKitLogging();          // Core, no OTEL (IServiceCollection extension)
services.AddMicroKitOpenTelemetry();    // OTEL bridge, optional (IServiceCollection extension)

// The consuming application configures the OTEL logging pipeline and exporter separately:
services.AddLogging(b => b.AddOpenTelemetry(opts => opts.AddOtlpExporter()));

// For trace correlation, register MicroKit ActivitySources with the TracerProvider:
services.AddOpenTelemetry()
    .WithTracing(t => t.AddMicroKitLoggingSources().AddOtlpExporter());
```

Note: `AddMicroKitOpenTelemetry` extends `IServiceCollection`, not `ILoggingBuilder`. It registers
a `MicroKitLogProcessor` into the OTEL logging pipeline via `IConfigureOptions<OpenTelemetryLoggerOptions>`.
The processor is owned by the `OpenTelemetryLoggerProvider` — not a DI singleton.

The bridge must not be auto-registered by any other MicroKit package.

## Sampling

- Never call `ActivitySource.StartActivity()` without checking `HasListeners()`
- Respect the OTEL sampler decision — do not create `Activity` objects when sampling says no
