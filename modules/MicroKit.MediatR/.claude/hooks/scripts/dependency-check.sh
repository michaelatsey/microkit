#!/usr/bin/env bash
set -euo pipefail

CHANGED_FILE="${1:-}"

# Only trigger on .csproj changes within MicroKit.MediatR
if [[ "$CHANGED_FILE" != *.csproj ]]; then
  exit 0
fi
if ! echo "$CHANGED_FILE" | grep -q "MicroKit.MediatR"; then
  exit 0
fi

echo "📦 Dependency check triggered by: $CHANGED_FILE"

PROJECT_NAME=$(basename "$CHANGED_FILE" .csproj)

# Rule 0: FluentAssertions is banned everywhere (commercial license)
if grep -q 'FluentAssertions' "$CHANGED_FILE" 2>/dev/null; then
  echo "❌ BLOCK [$PROJECT_NAME]: FluentAssertions is banned. Use Shouldly."
  exit 2
fi

# Rule 1: No inline versions anywhere
if grep -q 'PackageReference.*Version="' "$CHANGED_FILE" 2>/dev/null; then
  echo "❌ BLOCK [$PROJECT_NAME]: Inline Version= on PackageReference. Move to Directory.Packages.props."
  exit 2
fi

# Rule 2: Abstractions purity
if echo "$PROJECT_NAME" | grep -q "Abstractions"; then
  if grep -q '<ProjectReference' "$CHANGED_FILE"; then
    echo "❌ BLOCK [Abstractions]: ProjectReference forbidden (package refs only)."
    exit 2
  fi
  if grep -qE '<PackageReference[^>]*Include="MediatR"' "$CHANGED_FILE"; then
    echo "❌ BLOCK [Abstractions]: MediatR engine forbidden. Use MediatR.Contracts."
    exit 2
  fi
  for pkg in "FluentValidation" "Polly" "NSubstitute"; do
    if grep -q "$pkg" "$CHANGED_FILE"; then
      echo "❌ BLOCK [Abstractions]: $pkg forbidden in Abstractions (behavior/test concern)."
      exit 2
    fi
  done
fi

# Rule 3: Core must not pull behavior/test packages
if [ "$PROJECT_NAME" = "MicroKit.MediatR" ]; then
  for pkg in "FluentValidation" "Polly" "NSubstitute"; do
    if grep -q "$pkg" "$CHANGED_FILE"; then
      echo "❌ BLOCK [Core]: $pkg belongs in Behaviors/Testing, not core."
      exit 2
    fi
  done
fi

# Rule 4: Behaviors must not pull test packages
if echo "$PROJECT_NAME" | grep -q "Behaviors"; then
  if grep -q "NSubstitute" "$CHANGED_FILE"; then
    echo "❌ BLOCK [Behaviors]: NSubstitute is a test-only package."
    exit 2
  fi
fi

# Rule 5: Testing must not pull behavior packages (sibling isolation)
if echo "$PROJECT_NAME" | grep -q "Testing"; then
  for pkg in "FluentValidation" "Polly"; do
    if grep -q "$pkg" "$CHANGED_FILE"; then
      echo "❌ BLOCK [Testing]: $pkg belongs in Behaviors, not the test-helper package."
      exit 2
    fi
  done
  if grep -qE 'MicroKit\.MediatR\.Behaviors' "$CHANGED_FILE"; then
    echo "❌ BLOCK [Testing]: Testing must not reference Behaviors (sibling isolation)."
    exit 2
  fi
fi

echo "✅ Dependency check passed for $PROJECT_NAME."
exit 0
