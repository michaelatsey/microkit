# logging-analyzer-check Hook

Validates Roslyn analyzer files for correctness when `MicroKit.Logging.Analyzers` is modified.

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
            "command": "bash .claude/hooks/scripts/logging-analyzer-check.sh \"$CLAUDE_TOOL_INPUT_PATH\""
          }
        ]
      }
    ]
  }
}
```

## Shell Script

**Location:** `.claude/hooks/scripts/logging-analyzer-check.sh`

```bash
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

VIOLATIONS=0

# Check 1: DiagnosticId format
if grep -q 'DiagnosticId\s*=' "$CHANGED_FILE"; then
  if ! grep -qE 'DiagnosticId\s*=\s*"MKL[0-9]{4}"' "$CHANGED_FILE"; then
    echo "❌ BLOCK: DiagnosticId does not match MKLxxxx format."
    exit 2
  fi
fi

# Check 2: Mutable state on analyzer class (static field ok, instance field not ok)
if grep -qE '^\s+(private|protected|public|internal)\s+(?!static\s+readonly\s+Diagnostic)[a-zA-Z].*\s+[a-z]' "$CHANGED_FILE"; then
  echo "⚠️  WARNING: Possible mutable instance state in analyzer. Analyzers must be stateless."
  VIOLATIONS=$((VIOLATIONS + 1))
fi

# Check 3: LINQ usage in analyzer (forbidden)
if grep -nE '\.Where\(|\.Select\(|\.ToList\(|\.ToArray\(' "$CHANGED_FILE"; then
  echo "⚠️  WARNING: LINQ detected in analyzer. Use foreach over ImmutableArray<T>."
  VIOLATIONS=$((VIOLATIONS + 1))
fi

# Check 4: Every analyzer file should have a corresponding test
FILENAME=$(basename "$CHANGED_FILE" .cs)
MODULE_ROOT="$(git rev-parse --show-toplevel)/modules/MicroKit.Logging"
TEST_FILE="$MODULE_ROOT/tests/MicroKit.Logging.UnitTests/Analyzers/${FILENAME}Tests.cs"
if [[ "$FILENAME" == *"Analyzer"* ]] && [ ! -f "$TEST_FILE" ]; then
  echo "⚠️  WARNING: No test file found for $FILENAME. Expected: $TEST_FILE"
  VIOLATIONS=$((VIOLATIONS + 1))
fi

# Check 5: Build the analyzers project to catch compilation errors fast
dotnet build "$MODULE_ROOT/src/MicroKit.Logging.Analyzers/" --nologo -q 2>&1
if [ $? -ne 0 ]; then
  echo "❌ BLOCK: MicroKit.Logging.Analyzers does not compile."
  exit 2
fi

if [ $VIOLATIONS -gt 0 ]; then
  echo "⚠️  $VIOLATIONS warning(s). Run /logging-review-architecture or Use agent logging-analyzer-reviewer."
fi

echo "✅ Analyzer check passed."
exit 0
```

## What It Validates

| Check | Severity | Description |
|-------|----------|-------------|
| DiagnosticId format | BLOCK | Must be `MKLxxxx` |
| Mutable instance state | WARNING | Analyzers must be stateless |
| LINQ usage | WARNING | Use `foreach` over `ImmutableArray<T>` |
| Missing test file | WARNING | Every analyzer needs a test class |
| Compilation | BLOCK | Analyzer project must compile |

## Exit Codes

- **0** — pass (with optional warnings)
- **2** — block (DiagnosticId format violation or compilation failure)
