# /logging-review-observability

Invoke the `logging-observability-reviewer` agent on the OTEL bridge, ActivitySource, and correlation pipeline.

## Usage

```
/logging-review-observability [--scope <otel|diagnostics|correlation|all>]
```

**Examples:**
```
/logging-review-observability
/logging-review-observability --scope otel
/logging-review-observability --scope correlation
```

## Steps

```
1. Load .claude/rules/logging-opentelemetry.md and .claude/rules/logging-diagnostics.md
2. Load .claude-context/standards/logging-activity-names.md
3. Load .claude-context/standards/logging-diagnostics-events.md
4. Determine scope:
   - otel       → src/MicroKit.Logging.OpenTelemetry/
   - diagnostics → src/MicroKit.Logging.Diagnostics/
   - correlation → context propagation in src/MicroKit.Logging/
   - all (default) → all three
5. Use agent logging-observability-reviewer to analyze
6. Verify integration tests exist for W3C propagation scenarios
7. Output: correlation gaps, ActivitySource issues, OTEL bridge issues
```
