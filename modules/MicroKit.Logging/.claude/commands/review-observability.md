# /review-observability

Invoke the `observability-reviewer` agent on the OTEL bridge, ActivitySource, and correlation pipeline.

## Usage

```
/review-observability [--scope <otel|diagnostics|correlation|all>]
```

**Examples:**
```
/review-observability
/review-observability --scope otel
/review-observability --scope correlation
```

## Steps

```
1. Load .claude/rules/opentelemetry.md and .claude/rules/diagnostics.md
2. Load .claude-context/standards/activity-names.md
3. Load .claude-context/standards/diagnostics-events.md
4. Determine scope:
   - otel       → src/MicroKit.Logging.OpenTelemetry/
   - diagnostics → src/MicroKit.Logging.Diagnostics/
   - correlation → context propagation in src/MicroKit.Logging/
   - all (default) → all three
5. Use agent observability-reviewer to analyze
6. Verify integration tests exist for W3C propagation scenarios
7. Output: correlation gaps, ActivitySource issues, OTEL bridge issues
```
