# Standard: Performance Budget

**Concrete performance targets for MicroKit.Persistence operations.**

A PR that regresses any metric below by > 10% requires explicit `performance-reviewer` approval.

---

## Repository Operation Targets

| Operation | Overhead vs Raw EF | Allocation Target |
|-----------|-------------------|------------------|
| `FindAsync` (by PK, tracked) | < 1 ms | ≤ 2 allocations over raw `FindAsync` |
| `FindAsync` (by PK, no-tracking) | < 0.5 ms | ≤ 1 allocation over raw query |
| `ListAsync` (paged, 20 items, NoTracking) | < 2 ms | ≤ 3 allocations over raw query |
| `CommitAsync` (single aggregate) | < 0.5 ms | ≤ 1 allocation over raw `SaveChangesAsync` |
| `AnyAsync` (simple predicate) | < 0.3 ms | ≤ 1 allocation |
| `CountAsync` (simple predicate) | < 0.5 ms | ≤ 1 allocation |

---

## InMemoryRepository Targets (test helpers)

| Operation | Target |
|-----------|--------|
| `FindAsync` | < 0.1 ms, 0 allocations on warm path |
| `AddAsync` | < 0.1 ms |
| `ListAsync` | < 0.5 ms for 100 items |
| `CommitAsync` | < 0.1 ms (no-op after staging) |

---

## Allocation Policy

- **Zero allocation** on the synchronous fast path for `FindAsync` when the entity is in the
  identity map (EF Core's first-level cache) or the InMemoryRepository dictionary
- **No boxing** of value-type aggregate IDs in repository method call paths
- `ValueTask<T>` required — no `Task<T>` (avoids state-machine allocation on sync paths)
- `IReadOnlyList<T>` return type — avoids defensive copy at the call site

---

## Query Generation Budget

| Pattern | Max SQL Statements |
|---------|-------------------|
| `ListAsync` (no includes) | 1 SELECT |
| `ListAsync` (with 1 collection include) | 1 SELECT (single query) or 2 (split query) |
| `ListAsync` (with 2+ collection includes) | `AsSplitQuery()` required — N+1 of collections is acceptable |
| `CommitAsync` (single aggregate) | 1 INSERT / UPDATE / DELETE |
| `CommitAsync` (multiple aggregates) | 1 INSERT / UPDATE / DELETE per aggregate (batched) |

---

## Benchmark Baseline (BenchmarkDotNet)

```
BenchmarkDotNet v0.14+, .NET 10
[MemoryDiagnoser][SimpleJob(RuntimeMoniker.Net100)]
```

The benchmark project lives at `benchmarks/MicroKit.Persistence.Benchmarks/`.
Results are stored in `BenchmarkDotNet.Artifacts/` (gitignored).

Run: `dotnet run --project benchmarks/ -c Release --filter * -- --exporters markdown`
