#!/usr/bin/env bash
set -euo pipefail

CHANGED_FILE="${1:-}"

# Only trigger on files in MicroKit.Logging.Analyzers
if ! echo "$CHANGED_FILE" | grep -q "MicroKit.Logging.Analyzers"; then
  exit 0
fi
if [[ "$CHANGED_FILE" != *.cs ]]; then
  exit 0
fi

echo "🔬 Analyzer check triggered by: $(basename $CHANGED_FILE)"

MODULE_ROOT="$(git rev-parse --show-toplevel)/modules/MicroKit.Logging"
FILENAME=$(basename "$CHANGED_FILE" .cs)
VIOLATIONS=0

# Check 1: DiagnosticId format
if grep -q 'DiagnosticId\s*=' "$CHANGED_FILE"; then
  if ! grep -qE 'DiagnosticId\s*=\s*"MKL[0-9]{4}"' "$CHANGED_FILE"; then
    echo "❌ BLOCK: DiagnosticId does not match MKLxxxx format."
    exit 2
  fi
fi

# Check 2: LINQ usage in analyzer (forbidden)
if grep -nE '\.Where\(|\.Select\(|\.ToList\(|\.ToArray\(' "$CHANGED_FILE" 2>/dev/null; then
  echo "⚠️  WARNING: LINQ detected in analyzer. Use foreach over ImmutableArray<T>."
  VIOLATIONS=$((VIOLATIONS + 1))
fi

# Check 3: Every analyzer should have a corresponding test
TEST_FILE="$MODULE_ROOT/tests/MicroKit.Logging.UnitTests/Analyzers/${FILENAME}Tests.cs"
if echo "$FILENAME" | grep -q "Analyzer"; then
  if [ ! -f "$TEST_FILE" ]; then
    echo "⚠️  WARNING: No test file found for $FILENAME. Expected: $TEST_FILE"
    VIOLATIONS=$((VIOLATIONS + 1))
  fi
fi

# Check 4: Build the analyzers project
ANALYZERS_PROJECT="$MODULE_ROOT/src/MicroKit.Logging.Analyzers"
if [ -d "$ANALYZERS_PROJECT" ]; then
  dotnet build "$ANALYZERS_PROJECT" --nologo -q 2>&1
  if [ $? -ne 0 ]; then
    echo "❌ BLOCK: MicroKit.Logging.Analyzers does not compile."
    exit 2
  fi
fi

if [ $VIOLATIONS -gt 0 ]; then
  echo "⚠️  $VIOLATIONS warning(s). Run: Use agent logging-analyzer-reviewer"
fi

echo "✅ Analyzer check passed."
exit 0
