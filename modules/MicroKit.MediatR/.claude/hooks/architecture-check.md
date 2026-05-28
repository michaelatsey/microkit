# architecture-check Hook

Runs on `PostToolUse` (Edit|Write) for any `.csproj`, `.slnx`, or `.cs` file under
`modules/MicroKit.MediatR/`.

## Configuration

Wired in `.claude/settings.json`:

```json
{
  "type": "command",
  "command": "bash .claude/hooks/scripts/architecture-check.sh \"$CLAUDE_TOOL_INPUT_PATH\""
}
```

## Shell Script

**Location:** `.claude/hooks/scripts/architecture-check.sh`

## What It Validates

### On `.csproj` / `.slnx`
- **CPM** — no inline `Version=` on any `PackageReference`
- **Abstractions purity** — no `ProjectReference`, no MediatR engine (Contracts only), no FluentValidation/Polly/NSubstitute

### On `.cs` (handlers)
- **No `IMediator`** injected into a handler → BLOCK (use `IDomainEventDispatcher`)
- **ValueTask** — warns when a Command/Query handler returns `Task<T>` instead of `ValueTask<T>`

## Exit Codes

- `0` — pass
- `2` — BLOCK (architecture violation; Claude must fix before continuing)

## Scope

Silent pass for files outside `MicroKit.MediatR`.
