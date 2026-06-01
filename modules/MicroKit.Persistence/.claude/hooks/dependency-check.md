# Hook: Dependency Check

Triggered on every `.csproj` edit within `MicroKit.Persistence`.

## Checks

1. FluentAssertions banned
2. No inline `Version=` on `PackageReference`
3. Abstractions purity — no EF Core, Npgsql, SqlServer packages
4. Core isolation — no EF Core or provider packages
5. Provider isolation — PostgreSql and SqlServer confined to their projects
6. NSubstitute confined to Testing
7. Analyzers package referenced build-only

## Script

`.claude/hooks/scripts/dependency-check.sh`

## On Failure

Exit code 2 — the edit is blocked. The hook outputs the violated rule and suggests a fix.
