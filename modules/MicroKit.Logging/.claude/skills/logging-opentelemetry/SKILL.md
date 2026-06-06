# Skill: Logging OpenTelemetry

How to work with the OpenTelemetry bridge in MicroKit.Logging — reasoning, applying, verifying.

## When to Apply

Apply this skill when:
- Adding or modifying `MicroKit.Logging.OpenTelemetry`
- Implementing `ActivitySource`-based instrumentation
- Verifying W3C TraceContext propagation
- Debugging missing `TraceId`/`SpanId` in logs

## Core Concepts

### Signal Correlation

In a properly configured system, a single request produces three correlated signals:

```
Request
  ├── Logs     (via ILogger → OTEL Logs → OTLP)
  ├── Traces   (via ActivitySource → OTEL Traces → OTLP)
  └── Metrics  (future — not in v1)
```

The correlation key is `TraceId` + `SpanId`, extracted from `Activity.Current`.

### W3C TraceContext

```
traceparent: 00-{traceId}-{spanId}-{flags}
tracestate:  vendor1=value1,vendor2=value2
```

Always use `System.Diagnostics.Activity` APIs to parse/generate these — never manual string parsing.

## Verifying Correlation

```csharp
// In integration tests, verify TraceId appears in logs
using var activity = new ActivitySource("Test").StartActivity("TestOperation");
Assert.NotNull(activity);

// Log something
logger.LogInformation("Test log");

// Verify log has TraceId = activity.TraceId
var logEntry = capturedLogs.Last();
logEntry.State.Should().ContainKey(LogPropertyNames.TraceId)
    .WhoseValue.Should().Be(activity.TraceId.ToString());
```

## ActivitySource Registration

```csharp
// In OpenTelemetry provider setup
services.AddOpenTelemetry()
    .WithTracing(builder =>
    {
        builder.AddSource("MicroKit.Logging");
        builder.AddSource("MicroKit.*");  // All MicroKit modules
    });
```

## Debugging Missing Correlation

1. Verify `Activity.Current` is not null at the log site
2. Verify `ActivitySource.HasListeners()` returns true (OTEL not attached = no Activity)
3. Check `ConfigureAwait(false)` — Activity does NOT flow through `ConfigureAwait(false)` by default in some configurations
4. Verify `OTEL_PROPAGATORS=tracecontext` is set for distributed scenarios

## OTEL SDK Versions

Reference the current versions in `.claude-context/standards/` — OTEL SDK versions must match across all provider packages to avoid binding conflicts.
