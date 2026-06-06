#!/usr/bin/env bash
set -euo pipefail

CHANGED_FILE="${1:-}"

# Only trigger on .csproj changes within MicroKit.Logging
if [[ "$CHANGED_FILE" != *.csproj ]]; then
  exit 0
fi
if ! echo "$CHANGED_FILE" | grep -q "MicroKit.Logging"; then
  exit 0
fi

echo "📦 Dependency check triggered by: $CHANGED_FILE"

PROJECT_NAME=$(basename "$CHANGED_FILE" .csproj)

# Rule 1: No inline versions anywhere
if grep -q 'PackageReference.*Version="' "$CHANGED_FILE" 2>/dev/null; then
  echo "❌ BLOCK [$PROJECT_NAME]: Inline Version= on PackageReference. Move to Directory.Packages.props."
  exit 2
fi

# Rule 2: Abstractions must not have ProjectReference
if echo "$PROJECT_NAME" | grep -q "Abstractions"; then
  if grep -q '<ProjectReference' "$CHANGED_FILE"; then
    echo "❌ BLOCK [Abstractions]: ProjectReference forbidden in Abstractions project."
    exit 2
  fi
fi

# Rule 3: Abstractions must not reference MicroKit.Result
if echo "$PROJECT_NAME" | grep -q "Abstractions"; then
  if grep -q 'MicroKit.Result' "$CHANGED_FILE"; then
    echo "❌ BLOCK [Abstractions]: MicroKit.Result dependency forbidden (ADR-006)."
    exit 2
  fi
fi

# Rule 4: Core must not reference provider packages
if [ "$PROJECT_NAME" = "MicroKit.Logging" ]; then
  for pkg in "OpenTelemetry" "Serilog" "Microsoft.AspNetCore"; do
    if grep -q "$pkg" "$CHANGED_FILE"; then
      echo "❌ BLOCK [Core]: Forbidden reference to $pkg in MicroKit.Logging core."
      exit 2
    fi
  done
  # Core must not reference MicroKit.Result either (ADR-006)
  if grep -q 'MicroKit.Result' "$CHANGED_FILE"; then
    echo "❌ BLOCK [Core]: MicroKit.Result dependency forbidden (ADR-006)."
    exit 2
  fi
fi

# Rule 5: Cross-provider references forbidden
if echo "$PROJECT_NAME" | grep -q "OpenTelemetry"; then
  if grep -q "Serilog" "$CHANGED_FILE"; then
    echo "❌ BLOCK [$PROJECT_NAME]: Cross-provider reference to Serilog is forbidden."
    exit 2
  fi
fi
if echo "$PROJECT_NAME" | grep -q "Serilog"; then
  if grep -q "OpenTelemetry" "$CHANGED_FILE"; then
    echo "❌ BLOCK [$PROJECT_NAME]: Cross-provider reference to OpenTelemetry is forbidden."
    exit 2
  fi
fi

echo "✅ Dependency check passed for $PROJECT_NAME."
exit 0
