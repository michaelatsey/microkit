# Hook: Performance Check

Triggered on every `.cs` edit in `src/` within `MicroKit.Persistence`.

## Checks

- `Task<T>` return type on a repository method (should be `ValueTask<T>`)
- `await` without `ConfigureAwait(false)` in library code
- `CountAsync() > 0` pattern (should be `AnyAsync`)
- `.Result` or `.GetAwaiter().GetResult()` in async method

## Script

`.claude/hooks/scripts/performance-check.sh`

## Severity

- `Task<T>` on repository method → WARNING
- `.Result` / sync-over-async → ERROR (blocked)
- Missing `ConfigureAwait(false)` → WARNING
