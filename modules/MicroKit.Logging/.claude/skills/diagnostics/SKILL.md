# Skill: Diagnostics

How to reason about, apply, and verify the diagnostics layer of MicroKit.Logging. This skill is about **behavior and application**, not a catalog of values (see `.claude-context/standards/diagnostics-events.md` for exact names).

## When to Apply

Apply this skill when:
- Deciding whether to emit a `DiagnosticSource` event vs an `Activity` vs a log
- Adding instrumentation to a framework-level operation
- Verifying that correlation propagates correctly across async boundaries
- Debugging "missing events" in a DiagnosticListener consumer

## Decision Framework: What Signal to Use?

| Scenario | Use |
|----------|-----|
| Framework operation with timing (pipeline, enrichment) | `ActivitySource.StartActivity()` |
| Framework internal state change (enricher registered, scope created) | `DiagnosticSource.Write()` |
| Application-visible event worth logging | `ILogger` + structured properties |
| Error in framework logic | `ILogger.LogError` + rethrow |

## How to Emit a DiagnosticSource Event

```csharp
// 1. Always guard — never construct payload if nobody is listening
if (_diagnosticListener.IsEnabled(DiagnosticEventNames.EnrichmentExecuted))
{
    // 2. Payload is an anonymous object — document its shape in a comment
    // Shape: { EnricherCount: int, OperationId: string, ElapsedMs: long }
    _diagnosticListener.Write(DiagnosticEventNames.EnrichmentExecuted, new
    {
        EnricherCount = enricherCount,
        OperationId = operationId,
        ElapsedMs = elapsed.TotalMilliseconds
    });
}
```

## How to Use ActivitySource

```csharp
// Static, readonly — one instance per logical component
private static readonly ActivitySource _source = new("MicroKit.Logging", "1.0.0");

public async ValueTask ExecuteAsync(CancellationToken ct = default)
{
    // StartActivity returns null when no listener is attached — always null-check
    using var activity = _source.StartActivity("EnrichmentPipeline.Execute");
    activity?.SetTag(LogPropertyNames.OperationId, _context.OperationId);

    // ... do work ...

    activity?.SetStatus(ActivityStatusCode.Ok);
}
```

## Ensuring Propagation Across Async Boundaries

`Activity.Current` propagates through `await` via `ExecutionContext` — this works correctly.

`AsyncLocal<T>` also propagates through `await`.

**Known edge case:** `ConfigureAwait(false)` can detach from the synchronization context but does NOT prevent `Activity` or `AsyncLocal` propagation in .NET 5+. This is safe.

**Risky scenario:** manually spawning `Task.Run()` without capturing context first:

```csharp
// ❌ Activity may be null inside Task.Run
Task.Run(() => DoWork());

// ✅ Capture before spawning
var activity = Activity.Current;
Task.Run(() => { Activity.Current = activity; DoWork(); });
```

## Verifying Events in Tests

```csharp
var listener = new TestDiagnosticListener("MicroKit.Logging");
DiagnosticListener.AllListeners.Subscribe(listener);

// ... trigger the operation ...

listener.Events.Should().ContainSingle(e =>
    e.Name == DiagnosticEventNames.EnrichmentExecuted);
```
