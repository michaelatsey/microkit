#!/usr/bin/env bash
set -euo pipefail

CHANGED_FILE="${1:-}"

# Trigger on .csproj/.slnx (CPM + Abstractions purity) or .cs (handler/behavior contracts)
case "$CHANGED_FILE" in
  *.csproj|*.slnx|*.cs) ;;
  *) exit 0 ;;
esac

# Only trigger for MicroKit.MediatR files
if ! echo "$CHANGED_FILE" | grep -q "MicroKit.MediatR"; then
  exit 0
fi

echo "🏛️  Architecture check triggered by: $CHANGED_FILE"

MODULE_ROOT="$(git rev-parse --show-toplevel)/modules/MicroKit.MediatR"

# ── csproj / slnx checks ────────────────────────────────────────────────────
case "$CHANGED_FILE" in
  *.csproj|*.slnx)
    # CPM: no inline versions
    if grep -rE 'PackageReference.*Version="' "$MODULE_ROOT/src/" 2>/dev/null | grep -v '<!--'; then
      echo "❌ BLOCK: PackageReference with inline Version= found. Use Directory.Packages.props."
      exit 2
    fi

    # Abstractions purity: no ProjectReference, no MediatR engine (Contracts only)
    ABS_CSPROJ="$MODULE_ROOT/src/MicroKit.MediatR.Abstractions/MicroKit.MediatR.Abstractions.csproj"
    if [ -f "$ABS_CSPROJ" ]; then
      if grep -q '<ProjectReference' "$ABS_CSPROJ"; then
        echo "❌ BLOCK: MicroKit.MediatR.Abstractions has a ProjectReference — forbidden (package refs only)."
        exit 2
      fi
      if grep -qE '<PackageReference[^>]*Include="MediatR"' "$ABS_CSPROJ"; then
        echo "❌ BLOCK: Abstractions references the MediatR engine. Use MediatR.Contracts only."
        exit 2
      fi
      if grep -qE 'FluentValidation|Polly|NSubstitute' "$ABS_CSPROJ"; then
        echo "❌ BLOCK: Abstractions references a behavior/test package (FluentValidation/Polly/NSubstitute)."
        exit 2
      fi
    fi
    ;;
esac

# ── .cs checks: handler contract & no-coupling ──────────────────────────────
case "$CHANGED_FILE" in
  *.cs)
    if echo "$CHANGED_FILE" | grep -qiE 'Handler\.cs$'; then
      # No IMediator injected into a handler (indirect coupling to the pipeline)
      if grep -qE '\bIMediator\b' "$CHANGED_FILE"; then
        echo "❌ BLOCK: IMediator referenced in a handler. Use IDomainEventDispatcher for events."
        exit 2
      fi
      # Handlers return ValueTask, not Task (Command/Query handlers)
      if grep -qE 'Task<.*>\s+Handle\(' "$CHANGED_FILE" && ! grep -qE 'ValueTask<.*>\s+Handle\(' "$CHANGED_FILE"; then
        # Notification handlers legitimately return Task — only warn if it looks like a Command/Query handler
        if grep -qE 'I(Command|Query)Handler<' "$CHANGED_FILE"; then
          echo "⚠️  WARNING: Command/Query handler returns Task — prefer ValueTask. ($CHANGED_FILE)"
        fi
      fi
    fi
    ;;
esac

echo "✅ Architecture check passed."
exit 0
