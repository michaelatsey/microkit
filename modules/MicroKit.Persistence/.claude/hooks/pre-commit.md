# Hook: Pre-Commit

Runs before every commit that touches `modules/MicroKit.Persistence/` files.

## Checks (in order)

1. **FluentAssertions ban** — fail fast if any `.cs` or `.csproj` contains `FluentAssertions` or `.Should().`
2. **Build** — `dotnet build` in Debug configuration
3. **Unit tests** — fast, no database required
4. **Architecture tests** — EF Core not in Abstractions, IReadRepository purity, etc.

## Script

`.claude/hooks/scripts/pre-commit.sh`

## On Failure

The commit is blocked. Fix the listed issue, re-stage, and retry.
