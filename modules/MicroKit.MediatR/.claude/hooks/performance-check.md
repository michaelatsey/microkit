# performance-check Hook

Runs on `PostToolUse` (Edit|Write) for hot-path `.cs` files under
`modules/MicroKit.MediatR/src/` (behaviors, dispatch, handlers).

## Configuration

Wired in `.claude/settings.json`:

```json
{
  "type": "command",
  "command": "bash .claude/hooks/scripts/performance-check.sh \"$CLAUDE_TOOL_INPUT_PATH\""
}
```

## Shell Script

**Location:** `.claude/hooks/scripts/performance-check.sh`

## Hot-Path Triggers

Filenames matching `Behavior`, `BehaviorBase`, `MediatorExtensions`, `Dispatch`, `Pipeline`, or `*Handler.cs`.

## What It Warns On

1. Command/Query handler returning `Task<T>` instead of `ValueTask<T>`
2. `await` without `ConfigureAwait(false)` in library code
3. `IMediator` referenced in a behavior/handler (re-entrancy / coupling)
4. String interpolation in a log call (boxing — use a structured template / LoggerMessage)
5. LINQ in a behavior `Handle` method (verify it is not per-dispatch)

## Exit Codes

- `0` — pass (warnings are informational, non-blocking)

Warnings point to `/review-performance --file <path>` for a full analysis by the
`performance-reviewer` agent.
