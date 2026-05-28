#!/usr/bin/env bash
set -euo pipefail

MODULE_ROOT="$(git rev-parse --show-toplevel)/modules/MicroKit.MediatR"
CHANGED_FILES=$(git diff --cached --name-only)

# Only run if MicroKit.MediatR files are staged
if ! echo "$CHANGED_FILES" | grep -q "modules/MicroKit.MediatR/"; then
  exit 0
fi

echo "🔍 MicroKit.MediatR pre-commit checks..."

# 0. FluentAssertions is banned (commercial license) — fail fast
if grep -rn 'FluentAssertions\|\.Should()\.' "$MODULE_ROOT" \
     --include="*.cs" --include="*.csproj" 2>/dev/null | grep -v '<!--'; then
  echo "❌ FluentAssertions detected. Use Shouldly. Commit blocked."
  exit 1
fi

# 1. Build
echo "→ Building..."
dotnet build "$MODULE_ROOT/MicroKit.MediatR.slnx" -c Debug --nologo -q
if [ $? -ne 0 ]; then
  echo "❌ Build failed. Commit blocked."
  exit 1
fi

# 2. Unit tests only (fast — no integration/perf)
echo "→ Running unit tests..."
dotnet test "$MODULE_ROOT/tests/MicroKit.MediatR.UnitTests/" \
  --no-build --nologo -q \
  --logger "console;verbosity=minimal"
if [ $? -ne 0 ]; then
  echo "❌ Unit tests failed. Commit blocked."
  exit 1
fi

# 3. Architecture tests
echo "→ Running architecture tests..."
dotnet test "$MODULE_ROOT/tests/MicroKit.MediatR.ArchitectureTests/" \
  --no-build --nologo -q
if [ $? -ne 0 ]; then
  echo "❌ Architecture tests failed. Commit blocked."
  exit 1
fi

echo "✅ All pre-commit checks passed."
exit 0
