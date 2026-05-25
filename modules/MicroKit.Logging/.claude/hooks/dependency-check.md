# dependency-check Hook

Invokes the `dependency-guardian` agent automatically when any `<PackageReference>` or `<ProjectReference>` is added or changed.

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
            "command": "bash .claude/hooks/scripts/dependency-check.sh \"$CLAUDE_TOOL_INPUT_PATH\""
          }
        ]
      }
    ]
  }
}
```

## Shell Script

**Location:** `.claude/hooks/scripts/dependency-check.sh`

```bash
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

echo "Dependency check triggered by: $CHANGED_FILE"

MODULE_ROOT="$(git rev-parse --show-toplevel)/modules/MicroKit.Logging"
PROJECT_NAME=$(basename "$CHANGED_FILE" .csproj)

# Rule 1: No inline versions anywhere
if grep -q 'PackageReference.*Version="' "$CHANGED_FILE" 2>/dev/null; then
  echo "BLOCK [$PROJECT_NAME]: Inline Version= on PackageReference. Move to Directory.Packages.props."
  exit 2
fi

# Rule 2: Abstractions must not have ProjectReference
if echo "$PROJECT_NAME" | grep -q "Abstractions"; then
  if grep -q '<ProjectReference' "$CHANGED_FILE"; then
    echo "BLOCK [Abstractions]: ProjectReference forbidden in Abstractions project."
    exit 2
  fi
fi

# Rule 3: Core must not reference provider packages
if [ "$PROJECT_NAME" = "MicroKit.Logging" ]; then
  FORBIDDEN_REFS=("OpenTelemetry" "Serilog" "Microsoft.AspNetCore")
  for ref in "${FORBIDDEN_REFS[@]}"; do
    if grep -q "$ref" "$CHANGED_FILE"; then
      echo "BLOCK [Core]: Forbidden reference to $ref in MicroKit.Logging core."
      exit 2
    fi
  done
fi

# Rule 4: Cross-provider references forbidden
if echo "$PROJECT_NAME" | grep -qE "OpenTelemetry|Serilog"; then
  OTHER_PROVIDERS=("OpenTelemetry" "Serilog")
  CURRENT_PROVIDER=""
  for p in "${OTHER_PROVIDERS[@]}"; do
    if echo "$PROJECT_NAME" | grep -q "$p"; then
      CURRENT_PROVIDER="$p"
    fi
  done
  for p in "${OTHER_PROVIDERS[@]}"; do
    if [ "$p" != "$CURRENT_PROVIDER" ] && grep -q "$p" "$CHANGED_FILE"; then
      echo "BLOCK [$PROJECT_NAME]: Cross-provider reference to $p is forbidden."
      exit 2
    fi
  done
fi

echo Dependency check passed for $PROJECT_NAME."
exit 0
```

## What It Validates

1. **No inline package versions** — CPM enforcement
2. **Abstractions purity** — no `ProjectReference` in `Abstractions`
3. **Core isolation** — `MicroKit.Logging` core cannot depend on provider SDKs
4. **Cross-provider isolation** — OpenTelemetry and Serilog cannot reference each other

## Relationship to Agent

This hook handles **fast deterministic checks** via shell. For deeper graph analysis (transitive dependencies, NuGet resolution), invoke `/review-architecture` which runs the `dependency-guardian` agent.
