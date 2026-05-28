# dependency-check Hook

Runs on `PostToolUse` (Edit|Write) for any `.csproj` under `modules/MicroKit.MediatR/`.

## Configuration

Wired in `.claude/settings.json`:

```json
{
  "type": "command",
  "command": "bash .claude/hooks/scripts/dependency-check.sh \"$CLAUDE_TOOL_INPUT_PATH\""
}
```

## Shell Script

**Location:** `.claude/hooks/scripts/dependency-check.sh`

## What It Validates (4-layer graph)

- **FluentAssertions ban** — blocked in every project
- **CPM** — no inline `Version=`
- **Abstractions** — no `ProjectReference`, no MediatR engine, no FluentValidation/Polly/NSubstitute
- **Core (`MicroKit.MediatR`)** — no FluentValidation/Polly/NSubstitute
- **Behaviors** — no NSubstitute (test-only)
- **Testing** — no FluentValidation/Polly, and no reference to `MicroKit.MediatR.Behaviors` (sibling isolation)

## Exit Codes

- `0` — pass
- `2` — BLOCK (dependency violation)

## Related

The `dependency-guardian` agent performs the same validation in depth and can be invoked manually
via `/review-architecture` or after a `.csproj` change.
