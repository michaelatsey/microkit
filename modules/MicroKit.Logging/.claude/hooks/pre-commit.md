# pre-commit Hook

Runs before every commit on files within `modules/MicroKit.Logging/`.

## Claude Code Configuration

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "bash .claude/hooks/scripts/pre-commit.sh"
          }
        ]
      }
    ]
  }
}
```

> **Note:** To activate this hook in Claude Code, add the above configuration to `.claude/settings.json` at the module level.

## Shell Script

**Location:** `.claude/hooks/scripts/pre-commit.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail

MODULE_ROOT="$(git rev-parse --show-toplevel)/modules/MicroKit.Logging"
CHANGED_FILES=$(git diff --cached --name-only)

# Only run if MicroKit.Logging files are staged
if ! echo "$CHANGED_FILES" | grep -q "modules/MicroKit.Logging/"; then
  exit 0
fi

echo "MicroKit.Logging pre-commit checks..."

# 1. Build
echo "→ Building..."
dotnet build "$MODULE_ROOT/MicroKit.Logging.slnx" -c Debug --nologo -q
if [ $? -ne 0 ]; then
  echo "Build failed. Commit blocked."
  exit 1
fi

# 2. Unit tests only (fast — no integration/perf)
echo "→ Running unit tests..."
dotnet test "$MODULE_ROOT/tests/MicroKit.Logging.UnitTests/" \
  --no-build --nologo -q \
  --logger "console;verbosity=minimal"
if [ $? -ne 0 ]; then
  echo "Unit tests failed. Commit blocked."
  exit 1
fi

# 3. Architecture tests
echo "→ Running architecture tests..."
dotnet test "$MODULE_ROOT/tests/MicroKit.Logging.ArchitectureTests/" \
  --no-build --nologo -q
if [ $? -ne 0 ]; then
  echo "Architecture tests failed. Commit blocked."
  exit 1
fi

echo "All pre-commit checks passed."
exit 0
```

## What It Validates

1. **Build** — module compiles without errors in Debug configuration
2. **Unit tests** — fast tests only, no integration or performance tests
3. **Architecture tests** — dependency rules enforced via NetArchTest

## Scope

Only activates when files under `modules/MicroKit.Logging/` are staged. Silent pass for unrelated commits.

## Bypass

```bash
git commit --no-verify -m "..."
```

Use only in exceptional cases — never on `main`.
