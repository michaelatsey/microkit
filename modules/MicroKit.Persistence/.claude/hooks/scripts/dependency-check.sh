#!/usr/bin/env bash
set -euo pipefail

CHANGED_FILE="${1:-}"

# Only trigger on .csproj changes within MicroKit.Persistence
if [[ "$CHANGED_FILE" != *.csproj ]]; then
  exit 0
fi
if ! echo "$CHANGED_FILE" | grep -q "MicroKit.Persistence"; then
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

# Rule 2: Abstractions purity — no EF Core, no providers, no NSubstitute
if echo "$PROJECT_NAME" | grep -qE "^MicroKit\.Persistence\.Abstractions$"; then
  if grep -q '<ProjectReference' "$CHANGED_FILE"; then
    echo "❌ BLOCK [Abstractions]: ProjectReference forbidden (package refs only)."
    exit 2
  fi
  for pkg in "EntityFrameworkCore" "Npgsql" "SqlServer" "NSubstitute" "MicroKit.Persistence"; do
    if grep -q "$pkg" "$CHANGED_FILE"; then
      echo "❌ BLOCK [Abstractions]: $pkg forbidden in Abstractions."
      exit 2
    fi
  done
fi

# Rule 3: Core must not pull EF Core or provider packages
if [ "$PROJECT_NAME" = "MicroKit.Persistence" ]; then
  for pkg in "EntityFrameworkCore" "Npgsql" "SqlServer" "NSubstitute"; do
    if grep -q "$pkg" "$CHANGED_FILE"; then
      echo "❌ BLOCK [Core]: $pkg belongs in EntityFrameworkCore or provider projects, not core."
      exit 2
    fi
  done
fi

# Rule 4: EntityFrameworkCore must not pull provider packages
if [ "$PROJECT_NAME" = "MicroKit.Persistence.EntityFrameworkCore" ]; then
  for pkg in "Npgsql" "SqlServer"; do
    if grep -q "$pkg" "$CHANGED_FILE"; then
      echo "❌ BLOCK [EntityFrameworkCore]: $pkg is a provider package — belongs in provider project."
      exit 2
    fi
  done
  if grep -q "NSubstitute" "$CHANGED_FILE"; then
    echo "❌ BLOCK [EntityFrameworkCore]: NSubstitute is test-only."
    exit 2
  fi
fi

# Rule 5: PostgreSql project must only pull Npgsql
if echo "$PROJECT_NAME" | grep -q "PostgreSql"; then
  if grep -q "SqlServer" "$CHANGED_FILE"; then
    echo "❌ BLOCK [PostgreSql]: SqlServer package forbidden in PostgreSql provider."
    exit 2
  fi
fi

# Rule 6: SqlServer project must only pull SqlServer
if echo "$PROJECT_NAME" | grep -q "SqlServer"; then
  if grep -q "Npgsql" "$CHANGED_FILE"; then
    echo "❌ BLOCK [SqlServer]: Npgsql package forbidden in SqlServer provider."
    exit 2
  fi
fi

# Rule 7: Testing must not pull EF packages or provider packages
if echo "$PROJECT_NAME" | grep -q "Testing"; then
  for pkg in "EntityFrameworkCore" "Npgsql" "SqlServer"; do
    if grep -q "$pkg" "$CHANGED_FILE"; then
      echo "❌ BLOCK [Testing]: $pkg is an EF/provider concern — not in the test-helper package."
      exit 2
    fi
  done
fi

# Rule 8: Analyzers must not have runtime project references
if echo "$PROJECT_NAME" | grep -q "Analyzers"; then
  if grep -qE '<ProjectReference[^>]+MicroKit.Persistence' "$CHANGED_FILE"; then
    echo "❌ BLOCK [Analyzers]: ProjectReference to Persistence project forbidden in Analyzers (build-only)."
    exit 2
  fi
fi

echo "✅ Dependency check passed for $PROJECT_NAME."
exit 0
