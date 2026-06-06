# Workflow: Logging Adding Diagnostics

Step-by-step guide for adding a new `DiagnosticSource` event or `ActivitySource` instrumentation point.

## Decision: Activity vs DiagnosticSource?

| Use Activity when | Use DiagnosticSource when |
|------------------|--------------------------|
| Operation has duration | State change / point-in-time event |
| Needs distributed tracing | Internal framework notification |
| Should appear in OTEL traces | Consumers need structured payload |
| Correlates with spans | No tracing context needed |

## Steps

### 1. Name the Event

Check `.claude-context/standards/logging-activity-names.md` or `.claude-context/standards/logging-diagnostics-events.md`.

If the name doesn't exist: add it to the appropriate standards file first.

### 2. Implement

Load `.claude/skills/logging-diagnostics/SKILL.md` for implementation patterns.

For `DiagnosticSource`:
- Always guard with `IsEnabled()`
- Document payload shape in a comment above the `Write()` call

For `ActivitySource`:
- `static readonly ActivitySource` field
- Null-check the returned `Activity`
- Use `SetTag()` with `LogPropertyNames.*` constants

### 3. Verify Correlation

Write an integration test that verifies:
- `TraceId` in logs matches the `ActivityTraceId` when an `Activity` is active
- Event fires correctly and is receivable by a `DiagnosticListener`

### 4. Observability Review

Run: `/logging-review-observability --scope diagnostics`
