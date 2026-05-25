#!/usr/bin/env bash
set -euo pipefail

MODULE_ROOT="$(git rev-parse --show-toplevel)/modules/MicroKit.Logging"
CHANGED_FILES=$(git diff --cached --name-only)

# Only run if MicroKit.Logging files are staged
if ! echo "$CHANGED_FILES" | grep -q "modules/MicroKit.Logging/"; then
  exit 0
fi

echo "🔍 MicroKit.Logging pre-commit checks..."

# 1. Build
echo "→ Building..."
dotnet build "$MODULE_ROOT/MicroKit.Logging.slnx" -c Debug --nologo -q
if [ $? -ne 0 ]; then
  echo "❌ Build failed. Commit blocked."
  exit 1
fi

# 2. Unit tests only (fast — no integration/perf)
echo "→ Running unit tests..."
dotnet test "$MODULE_ROOT/tests/MicroKit.Logging.UnitTests/" \
  --no-build --nologo -q \
  --logger "console;verbosity=minimal"
if [ $? -ne 0 ]; then
  echo "❌ Unit tests failed. Commit blocked."
  exit 1
fi

# 3. Architecture tests
echo "→ Running architecture tests..."
dotnet test "$MODULE_ROOT/tests/MicroKit.Logging.ArchitectureTests/" \
  --no-build --nologo -q
if [ $? -ne 0 ]; then
  echo "❌ Architecture tests failed. Commit blocked."
  exit 1
fi

echo "✅ All pre-commit checks passed."
exit 0
