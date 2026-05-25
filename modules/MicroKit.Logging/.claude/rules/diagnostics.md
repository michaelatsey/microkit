# Rule: Diagnostics

Rules for `MicroKit.Logging.Diagnostics` — the `ActivitySource` and `DiagnosticSource` layer.

## ActivitySource

- All `ActivitySource` instances are `static readonly` class-level fields
- Naming format: `MicroKit.[Module].[Operation]` — see `.claude-context/standards/activity-names.md`
- `ActivitySource.StartActivity()` is always wrapped in a null check or `using` block
- Activities are never created in `finally` blocks, destructors, or catch blocks
- `Activity.SetTag()` uses only canonical property names from `LogPropertyNames`

```csharp
// ✅ Correct
private static readonly ActivitySource Source = new("MicroKit.Logging", "1.0.0");

using var activity = Source.StartActivity("EnrichmentPipeline.Execute");
activity?.SetTag(LogPropertyNames.OperationId, operationId);
```

## DiagnosticSource

- `DiagnosticSource` is used for internal framework events only — not for application-level tracing
- Always guard with `DiagnosticListener.IsEnabled(eventName)` before constructing the payload
- Event names follow the standard in `.claude-context/standards/diagnostics-events.md`
- Payloads are anonymous objects with a documented shape (comment above the event emission)

```csharp
// ✅ Correct
if (DiagnosticListener.IsEnabled(DiagnosticEventNames.EnrichmentExecuted))
{
    DiagnosticListener.Write(DiagnosticEventNames.EnrichmentExecuted, new
    {
        EnricherCount = count,
        OperationId = operationId,
        ElapsedMs = elapsed
    });
}
```

## Correlation Propagation

- `CorrelationId` propagates via `Activity` baggage for distributed scenarios
- `AsyncLocal` is the fallback for non-Activity contexts
- The two are kept in sync by `MicroKit.Logging` core — `Diagnostics` project only emits events
