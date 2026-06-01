#!/usr/bin/env bash
set -euo pipefail

CHANGED_FILE="${1:-}"

# Only trigger on C# source files in MicroKit.MediatR src/
if [[ "$CHANGED_FILE" != *.cs ]]; then
  exit 0
fi
if ! echo "$CHANGED_FILE" | grep -q "modules/MicroKit.MediatR/src/"; then
  exit 0
fi

# Hot-path file patterns (dispatch + behaviors)
HOT_PATH_PATTERNS=(
  "Behavior"
  "BehaviorBase"
  "MediatorExtensions"
  "Dispatch"
  "Pipeline"
)

FILENAME=$(basename "$CHANGED_FILE")
IS_HOT_PATH=false

for pattern in "${HOT_PATH_PATTERNS[@]}"; do
  if echo "$FILENAME" | grep -qi "$pattern"; then
    IS_HOT_PATH=true
    break
  fi
done

# Handlers are also perf-sensitive (ValueTask check applies)
if echo "$FILENAME" | grep -qiE 'Handler\.cs$'; then
  IS_HOT_PATH=true
fi

if [ "$IS_HOT_PATH" = false ]; then
  exit 0
fi

echo "⚡ Performance check triggered by hot-path file: $FILENAME"

VIOLATIONS=0

# Check 1: Command/Query handler returning Task instead of ValueTask
if echo "$FILENAME" | grep -qiE 'Handler\.cs$'; then
  if grep -qE 'I(Command|Query)Handler<' "$CHANGED_FILE" \
     && grep -qE '\bTask<' "$CHANGED_FILE" \
     && ! grep -qE 'ValueTask<' "$CHANGED_FILE"; then
    echo "⚠️  WARNING: Command/Query handler returns Task — prefer ValueTask for the synchronous fast path."
    VIOLATIONS=$((VIOLATIONS + 1))
  fi
fi

# Check 2: Missing ConfigureAwait(false) in library code
if grep -qE '\bawait\b' "$CHANGED_FILE"; then
  if ! grep -q 'ConfigureAwait(false)' "$CHANGED_FILE"; then
    echo "⚠️  WARNING: 'await' found but no ConfigureAwait(false). Library code must not capture context."
    VIOLATIONS=$((VIOLATIONS + 1))
  fi
fi

# Check 3: IMediator referenced in a behavior or handler (re-entrancy / coupling)
if echo "$FILENAME" | grep -qiE 'Behavior\.cs$|Handler\.cs$'; then
  if grep -qE '\bIMediator\b' "$CHANGED_FILE"; then
    echo "⚠️  WARNING: IMediator referenced in a behavior/handler — risk of re-entrant dispatch / coupling."
    VIOLATIONS=$((VIOLATIONS + 1))
  fi
fi

# Check 4: String interpolation in a log call (boxing / no structured template)
if grep -nE 'Log(Information|Warning|Error|Debug|Trace|Critical)\(\$"' "$CHANGED_FILE"; then
  echo "⚠️  WARNING: String interpolation in a log call. Use a structured template or LoggerMessage."
  VIOLATIONS=$((VIOLATIONS + 1))
fi

# Check 5: LINQ in a behavior hot path
if echo "$FILENAME" | grep -qiE 'Behavior\.cs$'; then
  if grep -nE '\.(Where|Select|ToList|ToArray|FirstOrDefault)\(' "$CHANGED_FILE"; then
    echo "⚠️  WARNING: Possible LINQ usage in a behavior. Verify it is not per-dispatch."
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
