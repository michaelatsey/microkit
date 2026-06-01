# Workflow: Reviewing Performance

Step-by-step guide for auditing query performance in MicroKit.Persistence.

## When to Use

- Before merging any change to a repository `ListAsync`, `FindAsync`, or `CommitAsync`
- When a benchmark delta > 10% is observed in CI
- When a consumer reports N+1 or high DB query latency

## Steps

### 1. Run `/review-performance`

The `performance-reviewer` agent audits the changed files against:
- `.claude/rules/performance.md`
- `.claude-context/standards/performance-budget.md`

### 2. Run `/audit-queries`

Detects at the code level:
- Missing `AsNoTracking()` — Analyzer PRDANA003
- `SaveChanges` in read repos — Analyzer PRDANA002
- `DbContext` in query handlers — Analyzer PRDANA001
- Navigation properties accessed without explicit `.Include()` (potential lazy load)

### 3. Run Benchmarks

```bash
dotnet run --project benchmarks/MicroKit.Persistence.Benchmarks/ -c Release --filter *
```

Focus metrics:
- `FindAsync` overhead vs raw `DbContext.FindAsync`
- `ListAsync` (paged, NoTracking) overhead vs raw query
- `CommitAsync` overhead vs raw `SaveChangesAsync`
- Allocations per operation (Gen0/Gen1/Gen2)

### 4. Check EF Core Query Plan

```csharp
// Enable query logging temporarily
optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information)
              .EnableSensitiveDataLogging();
```

Review generated SQL in logs: look for:
- `SELECT *` (projection missing)
- Multiple queries per request (N+1)
- `OFFSET 0 ROWS FETCH NEXT ...` without an ORDER BY (non-deterministic pagination)

### 5. Identify and Fix

Common fixes:
- Add `.Select(Dto.Projection)` for server-side projection
- Add `.Include()` via QueryOptions for navigation properties
- Use `AnyAsync` instead of `CountAsync() > 0`
- Use compiled queries for hot paths
- Use `AsSplitQuery()` for multi-collection includes

### 6. Re-run Benchmarks

Confirm the fix does not regress other metrics before approving the PR.
