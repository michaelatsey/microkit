---

name: logging-diagnostics
description: Use this skill when designing, implementing, reviewing, debugging, or validating diagnostics instrumentation in MicroKit.Logging, including ActivitySource, DiagnosticSource, correlation propagation, and observability behavior.
-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

# Purpose

Provide guidance for selecting the appropriate diagnostics signal, implementing instrumentation correctly, validating correlation propagation, and troubleshooting diagnostics behavior within MicroKit.Logging.

This skill focuses on diagnostics behavior and instrumentation strategy rather than specific event names or constants.

## When to Use

Use this skill when:

* Adding diagnostics instrumentation to a framework component.
* Deciding between ActivitySource, DiagnosticSource, and ILogger.
* Reviewing observability-related pull requests.
* Investigating missing diagnostics events.
* Troubleshooting correlation propagation issues.
* Validating OpenTelemetry compatibility.
* Designing new logging pipeline instrumentation.

## Decision Framework

Choose the most appropriate signal based on the purpose of the information.

| Scenario                            | Recommended Signal              |
| ----------------------------------- | ------------------------------- |
| Timed framework operation           | ActivitySource                  |
| Distributed tracing and correlation | ActivitySource                  |
| Internal framework state change     | DiagnosticSource                |
| Internal diagnostics event          | DiagnosticSource                |
| Application-visible business event  | ILogger                         |
| Framework error condition           | ILogger + exception propagation |

### Signal Selection Rules

Prefer ActivitySource when:

* Duration matters.
* Correlation matters.
* OpenTelemetry integration is expected.
* Parent-child operation relationships exist.

Prefer DiagnosticSource when:

* The event represents an internal framework state change.
* Consumers may subscribe for advanced diagnostics.
* No timing information is required.

Prefer ILogger when:

* The event should be visible in application logs.
* Operators need to investigate production behavior.
* The event contributes to operational observability.

## Implementing DiagnosticSource Events

### Required Process

1. Check whether listeners are enabled.
2. Avoid payload construction when unused.
3. Emit minimal payloads.
4. Document payload shape.
5. Avoid allocations on hot paths.

Example:

```csharp
if (_diagnosticListener.IsEnabled(EventName))
{
    // Shape:
    // {
    //     PropertyA: string,
    //     PropertyB: int
    // }

    _diagnosticListener.Write(EventName, new
    {
        PropertyA = valueA,
        PropertyB = valueB
    });
}
```

### Requirements

* Always call `IsEnabled()` before constructing payloads.
* Keep payloads small and stable.
* Avoid expensive object creation.
* Avoid exposing implementation details.
* Ensure payload contracts remain backward compatible.

## Implementing ActivitySource Instrumentation

### Required Process

1. Define a static ActivitySource.
2. Start an activity around the logical operation.
3. Add meaningful tags.
4. Set status appropriately.
5. Dispose the activity correctly.

Example:

```csharp
private static readonly ActivitySource Source =
    new("MicroKit.Logging", "1.0.0");

public async ValueTask ExecuteAsync(CancellationToken ct = default)
{
    using var activity =
        Source.StartActivity("Operation.Execute");

    activity?.SetTag("operation.id", operationId);

    // Execute operation

    activity?.SetStatus(ActivityStatusCode.Ok);
}
```

### Requirements

* Use one ActivitySource per logical component.
* Always null-check the returned Activity.
* Use stable activity names.
* Use tags instead of custom payload objects.
* Capture meaningful correlation identifiers.

## Correlation Propagation

### Expected Behavior

The following propagate automatically:

* Activity.Current across await boundaries.
* AsyncLocal<T> across await boundaries.
* ExecutionContext-based correlation.

### Safe Usage

```csharp
await operation.ConfigureAwait(false);
```

`ConfigureAwait(false)` does not break Activity propagation in modern .NET runtimes.

### Risky Usage

Manual task creation may lose correlation context.

Avoid:

```csharp
Task.Run(() => Execute());
```

Prefer:

```csharp
var activity = Activity.Current;

Task.Run(() =>
{
    Activity.Current = activity;
    Execute();
});
```

### Review Guidance

Whenever Task.Run, ThreadPool APIs, background workers, channels, or custom schedulers are introduced, verify correlation propagation explicitly.

## Verifying Diagnostics in Tests

Validation strategy:

1. Register a test listener.
2. Execute the operation.
3. Verify expected events.
4. Verify expected activity tags.
5. Verify correlation identifiers.

Example:

```csharp
var listener =
    new TestDiagnosticListener("MicroKit.Logging");

DiagnosticListener.AllListeners.Subscribe(listener);

// Execute operation

listener.Events.Should()
    .ContainSingle(e => e.Name == ExpectedEvent);
```

## Common Diagnostics Failures

### Missing DiagnosticSource Events

Possible causes:

* Listener not registered.
* Incorrect event name.
* IsEnabled() returning false.
* Event emitted before subscription.

### Missing Activity Data

Possible causes:

* No ActivityListener configured.
* ActivitySource mismatch.
* Activity disposed too early.
* Correlation lost during task scheduling.

### Missing Correlation

Possible causes:

* Manual Task.Run usage.
* Background thread execution.
* Custom scheduling infrastructure.
* Context not captured explicitly.

## Anti-Patterns

Avoid:

* Emitting both Activity and DiagnosticSource for the same semantic event without justification.
* Creating payloads before IsEnabled checks.
* Adding large object graphs to diagnostics payloads.
* Using ILogger as a tracing mechanism.
* Using DiagnosticSource for operational logging.
* Creating ActivitySource instances per request.
* Using unstable activity names.

## Best Practices

* Prefer ActivitySource for observable operations.
* Keep diagnostics payloads minimal.
* Preserve correlation whenever execution changes threads.
* Design instrumentation with OpenTelemetry compatibility in mind.
* Validate instrumentation with automated tests.
* Review allocation impact on hot paths.

## Validation Checklist

* [ ] Correct signal type selected.
* [ ] ActivitySource used for timed operations.
* [ ] DiagnosticSource used for internal state events.
* [ ] ILogger used for operational visibility.
* [ ] Correlation propagates correctly.
* [ ] Activity tags are meaningful.
* [ ] Payload allocations minimized.
* [ ] Diagnostics behavior covered by tests.
* [ ] OpenTelemetry compatibility preserved.
