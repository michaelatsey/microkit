# pre-commit Hook

Runs before every commit on files within `modules/MicroKit.MediatR/`.

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

> **Note:** This hook is intended as a Git `pre-commit` hook (or a `PreToolUse` Bash guard).
> The module's `.claude/settings.json` wires the per-edit checks (architecture/dependency/performance)
> on `PostToolUse`. Add the block above if you also want a commit-time gate inside Claude Code.

## Shell Script

**Location:** `.claude/hooks/scripts/pre-commit.sh`

## What It Validates

0. **FluentAssertions ban** — fails fast if `FluentAssertions` or `.Should().` appears (use Shouldly)
1. **Build** — module compiles without errors in Debug configuration
2. **Unit tests** — fast tests only, no integration or performance tests
3. **Architecture tests** — CQRS + dependency rules enforced via NetArchTest

## Scope

Only activates when files under `modules/MicroKit.MediatR/` are staged. Silent pass for unrelated commits.

## Bypass

```bash
git commit --no-verify -m "..."
```

Use only in exceptional cases — never on `main`.
