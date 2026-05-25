#!/usr/bin/env bash
set -euo pipefail

CHANGED_FILE="${1:-}"

# Only trigger on .csproj or .slnx changes
if [[ "$CHANGED_FILE" != *.csproj ]] && [[ "$CHANGED_FILE" != *.slnx ]]; then
  exit 0
fi

# Only trigger for MicroKit.Logging files
if ! echo "$CHANGED_FILE" | grep -q "MicroKit.Logging"; then
  exit 0
fi

echo "🏛️  Architecture check triggered by: $CHANGED_FILE"

MODULE_ROOT="$(git rev-parse --show-toplevel)/modules/MicroKit.Logging"

# Check for version attributes in csproj (CPM violation)
if grep -r 'PackageReference.*Version="' "$MODULE_ROOT/src/" 2>/dev/null | grep -v '<!--'; then
  echo "❌ BLOCK: PackageReference with inline Version= found. Use Directory.Packages.props."
  exit 2
fi

# Check Abstractions project for forbidden dependencies
ABSTRACTIONS_CSPROJ="$MODULE_ROOT/src/MicroKit.Logging.Abstractions/MicroKit.Logging.Abstractions.csproj"
if [ -f "$ABSTRACTIONS_CSPROJ" ]; then
  if grep -q '<ProjectReference' "$ABSTRACTIONS_CSPROJ"; then
    echo "❌ BLOCK: MicroKit.Logging.Abstractions has ProjectReference — forbidden."
    exit 2
  fi
  PACKAGES=$(grep '<PackageReference' "$ABSTRACTIONS_CSPROJ" | grep -v 'Microsoft.Extensions.Logging.Abstractions' || true)
  if [ -n "$PACKAGES" ]; then
    echo "❌ BLOCK: MicroKit.Logging.Abstractions has unauthorized PackageReference:"
    echo "$PACKAGES"
    exit 2
  fi
fi

echo "✅ Architecture check passed."
exit 0
