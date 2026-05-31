#!/usr/bin/env bash
set -euo pipefail

CHANGED_FILE="${1:-}"

# Only trigger on .cs files in src/
if [[ "$CHANGED_FILE" != *.cs ]]; then
  exit 0
fi
if ! echo "$CHANGED_FILE" | grep -q "MicroKit.Persistence"; then
  exit 0
fi
if echo "$CHANGED_FILE" | grep -q "/tests/"; then
  exit 0
fi

echo "⚡ Performance check triggered by: $CHANGED_FILE"

EXIT_CODE=0

# Rule 1: sync-over-async — blocked
if grep -qE '\.(Result|GetAwaiter\(\)\.GetResult\(\)|Wait\(\))' "$CHANGED_FILE" 2>/dev/null; then
  echo "❌ BLOCK: sync-over-async (.Result / .GetAwaiter().GetResult() / .Wait()) found in library code."
  echo "   Cause: thread-pool starvation and deadlock risk. Use await instead."
  EXIT_CODE=2
fi

# Rule 2: Task<T> on repository methods (warning)
if echo "$CHANGED_FILE" | grep -qiE 'Repository\.cs$'; then
  if grep -qE 'public\s+(async\s+)?Task<' "$CHANGED_FILE" 2>/dev/null; then
    echo "⚠️  WARNING: Repository method returns Task<T> — prefer ValueTask<T> to avoid state-machine allocation on synchronous paths."
  fi
fi

# Rule 3: CountAsync() > 0 pattern (warning)
if grep -qE 'CountAsync\(\s*[^)]*\)\s*[><=!]=?\s*0' "$CHANGED_FILE" 2>/dev/null; then
  echo "⚠️  WARNING: CountAsync() > 0 found — use AnyAsync() instead for existence checks."
fi

# Rule 4: Missing ConfigureAwait(false) on awaits in library code
if grep -qE 'await\s+' "$CHANGED_FILE" 2>/dev/null; then
  AWAITS_WITHOUT_CONFIGURE=$(grep -cE 'await\s+' "$CHANGED_FILE" 2>/dev/null || true)
  AWAITS_WITH_CONFIGURE=$(grep -cE '\.ConfigureAwait\(false\)' "$CHANGED_FILE" 2>/dev/null || true)
  if [ "$AWAITS_WITHOUT_CONFIGURE" -gt "$AWAITS_WITH_CONFIGURE" ]; then
    echo "⚠️  WARNING: Some awaits may be missing .ConfigureAwait(false) in library code."
    echo "   All await calls in MicroKit.Persistence library code must use .ConfigureAwait(false)."
  fi
fi

if [ "$EXIT_CODE" -eq 0 ]; then
  echo "✅ Performance check passed."
fi

exit $EXIT_CODE
