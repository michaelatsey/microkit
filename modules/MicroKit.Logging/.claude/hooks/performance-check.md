# performance-check Hook

Automatically flags potential performance regressions when hot-path files are modified. Triggers the `performance-reviewer` agent via a PostToolUse hook.

## Claude Code Configuration

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Edit|Write",
        "hooks": [
          {
            "type": "command",
            "command": "bash .claude/hooks/scripts/performance-check.sh \"$CLAUDE_TOOL_INPUT_PATH\""
          }
        ]
      }
    ]
  }
}
```

## Shell Script

**Location:** `.claude/hooks/scripts/performance-check.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail

CHANGED_FILE="${1:-}"

# Only trigger on C# source files in MicroKit.Logging src/
if [[ "$CHANGED_FILE" != *.cs ]]; then
  exit 0
fi
if ! echo "$CHANGED_FILE" | grep -q "modules/MicroKit.Logging/src/"; then
  exit 0
fi

# Hot-path file patterns — checked by filename
HOT_PATH_PATTERNS=(
  "EnrichmentPipeline"
  "OperationContext"
  "LogEnricher"
  "LogScope"
  "ContextAccessor"
  "CorrelationContext"
  "ActivityBridge"
)

FILENAME=$(basename "$CHANGED_FILE")
IS_HOT_PATH=false

for pattern in "${HOT_PATH_PATTERNS[@]}"; do
  if echo "$FILENAME" | grep -qi "$pattern"; then
    IS_HOT_PATH=true
    break
  fi
done

if [ "$IS_HOT_PATH" = false ]; then
  exit 0
fi

echo "⚡ Performance check triggered by hot-path file: $FILENAME"

# Static analysis: detect obvious regressions
VIOLATIONS=0

# Check 1: String interpolation in log calls
if grep -nE 'Log(Information|Warning|Error|Debug|Trace|Critical)\(\$"' "$CHANGED_FILE"; then
  echo "⚠️  WARNING: String interpolation detected in log call. Use structured template."
  VIOLATIONS=$((VIOLATIONS + 1))
fi

# Check 2: LINQ on hot path (basic detection)
if grep -nE '\.(Where|Select|ToList|ToArray|FirstOrDefault)\(' "$CHANGED_FILE"; then
  echo "⚠️  WARNING: Possible LINQ usage detected in hot-path file. Verify it's not per-invocation."
  VIOLATIONS=$((VIOLATIONS + 1))
fi

# Check 3: new List/Dictionary without capacity in enrichers
if grep -nE 'new (List|Dictionary)<' "$CHANGED_FILE" | grep -v '(capacity\|new Dictionary.*{)'; then
  echo "⚠️  INFO: Collection instantiation without capacity. Consider pre-allocating."
fi

# Check 4: Missing IsEnabled guard before property computation
if grep -nE 'LogLevel\.' "$CHANGED_FILE" | grep -v 'IsEnabled' > /dev/null 2>&1; then
  if ! grep -q 'IsEnabled' "$CHANGED_FILE"; then
    echo "⚠️  WARNING: LogLevel referenced but IsEnabled guard not found."
    VIOLATIONS=$((VIOLATIONS + 1))
  fi
fi

if [ $VIOLATIONS -gt 0 ]; then
  echo ""
  echo "⚠️  $VIOLATIONS performance warning(s) found in $FILENAME."
  echo "   Run /review-performance --file $CHANGED_FILE for full analysis."
  # Exit 0: warn but don't block (analysis required)
fi

echo "✅ Performance check complete."
exit 0
```

## Behavior

- **Warnings only** — this hook never blocks. It surfaces issues for review.
- **Hot-path detection** by filename pattern — not all C# files are checked
- For blocking enforcement, run `/review-performance` explicitly

## Checked Patterns

| Pattern | Severity | Reason |
|---------|----------|--------|
| `$"..."` in log call | WARNING | Allocates string even if log level disabled |
| LINQ on hot path | WARNING | Allocations + overhead per invocation |
| Collection without capacity | INFO | Potential resize allocations |
| No `IsEnabled` guard | WARNING | Expensive property computed even if log skipped |
