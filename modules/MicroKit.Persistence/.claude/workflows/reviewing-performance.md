# Workflow: Reviewing Performance

## When

- A change to `EfRepository` (aggregate load, `AggregateIncludePlan`), `EfReadRepository`,
  `EfSpecificationEvaluator`, `EfUnitOfWork` or the in-memory doubles
- A consumer reports N+1 queries, slow pages or high memory

## 1. Run `performance-reviewer`

Against `.claude/rules/performance.md` and `.claude-context/standards/performance-budget.md`.

## 2. Read the code paths

- Tracking on read paths (no analyzer — review it)
- `SaveChanges` in a read repository — MKP001; `DbContext` in a handler — MKP004
- Round trips of the aggregate load: 1, or 1 + C when split
- Navigations used without being loaded

## 3. Look at the SQL

There is no benchmark project. Log the SQL from an integration test:

```csharp
optionsBuilder.LogTo(output.WriteLine, LogLevel.Information).EnableSensitiveDataLogging();
// or, for one query: query.ToQueryString()
```

Look for: several statements per request (N+1), a Cartesian product from multiple collection
joins, `OFFSET … FETCH` without `ORDER BY`, columns a DTO read does not need.

## 4. Fix

Server-side projection, `Includes` (read side) or `IsOutsideAggregate()` (write side), `AsSplitQuery()`,
`AnyAsync` over `CountAsync() > 0`, ordering on paginated queries, a compiled query on a measured
hot path.

## 5. Prove it

An integration test that pins the behaviour (statement count, rows loaded). A claim about time or
allocations needs a measurement; without a benchmark project, say it is unmeasured.
