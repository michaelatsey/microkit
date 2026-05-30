#!/usr/bin/env bash
set -euo pipefail

CHANGED_FILE="${1:-}"

# Trigger on .csproj/.slnx or .cs files
case "$CHANGED_FILE" in
  *.csproj|*.slnx|*.cs) ;;
  *) exit 0 ;;
esac

# Only trigger for MicroKit.Persistence files
if ! echo "$CHANGED_FILE" | grep -q "MicroKit.Persistence"; then
  exit 0
fi

echo "🏛️  Architecture check triggered by: $CHANGED_FILE"

MODULE_ROOT="$(git rev-parse --show-toplevel)/modules/MicroKit.Persistence"

# ── csproj / slnx checks ────────────────────────────────────────────────────
case "$CHANGED_FILE" in
  *.csproj|*.slnx)
    # CPM: no inline versions in src/
    if grep -rE 'PackageReference.*Version="' "$MODULE_ROOT/src/" 2>/dev/null | grep -v '<!--'; then
      echo "❌ BLOCK: PackageReference with inline Version= found in src/. Use Directory.Packages.props."
      exit 2
    fi

    # Abstractions purity: no ProjectReference, no EF Core
    ABS_CSPROJ="$MODULE_ROOT/src/MicroKit.Persistence.Abstractions/MicroKit.Persistence.Abstractions.csproj"
    if [ -f "$ABS_CSPROJ" ]; then
      if grep -q '<ProjectReference' "$ABS_CSPROJ"; then
        echo "❌ BLOCK: MicroKit.Persistence.Abstractions has a ProjectReference — forbidden."
        exit 2
      fi
      if grep -qE 'EntityFrameworkCore|Npgsql|SqlServer' "$ABS_CSPROJ"; then
        echo "❌ BLOCK: Abstractions references an EF Core or provider package."
        exit 2
      fi
    fi
    ;;
esac

# ── .cs checks ──────────────────────────────────────────────────────────────
case "$CHANGED_FILE" in
  *.cs)
    # IReadRepository implementations must not call SaveChanges
    if echo "$CHANGED_FILE" | grep -qiE 'ReadRepository'; then
      if grep -qE '\bSaveChanges(Async)?\b' "$CHANGED_FILE"; then
        echo "❌ BLOCK: SaveChanges[Async] found in a ReadRepository implementation. Read repos must not commit."
        exit 2
      fi
    fi

    # Repository methods should return ValueTask, not Task
    if echo "$CHANGED_FILE" | grep -qiE 'Repository\.cs$'; then
      if grep -qE 'public\s+async\s+Task<' "$CHANGED_FILE" && ! grep -qE 'public\s+async\s+ValueTask<' "$CHANGED_FILE"; then
        echo "⚠️  WARNING: Repository method returns Task — prefer ValueTask. ($CHANGED_FILE)"
      fi
    fi

    # DbContext injected in Handler
    if echo "$CHANGED_FILE" | grep -qiE 'Handler\.cs$'; then
      if grep -qE '\bDbContext\b' "$CHANGED_FILE"; then
        echo "❌ BLOCK: DbContext referenced in a handler. Inject a typed repository instead. (PRDANA001)"
        exit 2
      fi
    fi
    ;;
esac

echo "✅ Architecture check passed."
exit 0
