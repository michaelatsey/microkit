# Hook: Architecture Check

Triggered on every `.csproj`, `.slnx`, or `.cs` edit within `MicroKit.Persistence`.

## Checks

### .csproj / .slnx
- No inline `Version=` on `PackageReference` in `src/`
- Abstractions purity: no ProjectReference, no EF Core packages

### .cs files
- Repository implementations return `ValueTask<T>` not `Task<T>`
- `IReadRepository` implementations do not call `SaveChanges[Async]`
- No `DbContext` injected directly into a class with `Handler` in its name

## Script

`.claude/hooks/scripts/architecture-check.sh`

## On Failure

Exit code 2 — the edit is blocked with the specific rule violated.
