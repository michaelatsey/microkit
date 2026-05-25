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

# Hot-path file patterns
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

VIOLATIONS=0

# Check 1: String interpolation in log calls
if grep -nE 'Log(Information|Warning|Error|Debug|Trace|Critical)\(\$"' "$CHANGED_FILE"; then
  echo "⚠️  WARNING: String interpolation in log call. Use structured template."
  VIOLATIONS=$((VIOLATIONS + 1))
fi

# Check 2: LINQ on hot path
if grep -nE '\.(Where|Select|ToList|ToArray|FirstOrDefault)\(' "$CHANGED_FILE"; then
  echo "⚠️  WARNING: Possible LINQ usage in hot-path file. Verify it is not per-invocation."
  VIOLATIONS=$((VIOLATIONS + 1))
fi

# Check 3: Missing IsEnabled guard
if grep -nE 'LogLevel\.' "$CHANGED_FILE" > /dev/null 2>&1; then
  if ! grep -q 'IsEnabled' "$CHANGED_FILE"; then
    echo "⚠️  WARNING: LogLevel referenced but IsEnabled guard not found."
    VIOLATIONS=$((VIOLATIONS + 1))
  fi
fi

if [ $VIOLATIONS -gt 0 ]; then
  echo ""
  echo "⚠️  $VIOLATIONS performance warning(s) in $FILENAME."
  echo "   Run /review-performance --file $CHANGED_FILE for full analysis."
fi

echo "✅ Performance check complete."
exit 0
