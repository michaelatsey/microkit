# Standard: Performance Budget

Targets for MicroKit.Persistence operations. **No benchmark project exists yet**: these are
review targets, not measured baselines or a CI gate. When a benchmark project is added, the
budget becomes measurable and a regression above 10% needs `performance-reviewer` approval.

---

## Read side — `EfReadRepository`

| Operation | Overhead vs equivalent raw EF Core query | Allocations over it |
|-----------|------------------------------------------|---------------------|
| `FindAsync` (by key) | < 1 ms | ≤ 2 over raw `FindAsync` |
| `ListAsync` (paged, 20 items, untracked) | < 2 ms | ≤ 3 |
| `AnyAsync` (simple predicate) | < 0.3 ms | ≤ 1 |
| `CountAsync` (simple predicate) | < 0.5 ms | ≤ 1 |

## Write side — `EfRepository`

`FindAsync(TId)` is a whole-aggregate load, not a primary-key lookup: one tracked query, or
1 + C round trips when the aggregate has C ≥ 2 collections (split). Its cost is that of the
aggregate, so it has no fixed overhead target; the review checks the round-trip count and that the
load stays inside the aggregate boundary. A tracked hit returns without a query.

| Operation | Target |
|-----------|--------|
| `CommitAsync` (single aggregate) | < 0.5 ms and ≤ 1 allocation over raw `SaveChangesAsync` |

## In-memory doubles (tests)

| Operation | Target |
|-----------|--------|
| `FindAsync`, `AddAsync`, `DeleteAsync` | < 0.1 ms, synchronous completion |
| `ListAsync` | < 0.5 ms for 100 items |

---

## Allocation policy

- `ValueTask` on repository and UoW methods: a tracked hit or an in-memory call completes
  synchronously without a task allocation.
- Per-model work is cached once (`AggregateIncludePlan` per entity type, key-type probes), never
  recomputed per call.
- `IReadOnlyList<T>` results — no defensive copy at the call site.

## SQL statements

| Pattern | Max statements |
|---------|----------------|
| `ListAsync`, no include | 1 |
| `ListAsync`, one collection include | 1 (or 2 with `AsSplitQuery()`) |
| `ListAsync`, ≥ 2 collection includes | `AsSplitQuery()` required: 1 + collections |
| `ListPagedAsync` | 2 (count, page) + split queries of the page |
| `EfRepository.FindAsync` | 1, or 1 + C when split |
| `CommitAsync` | the provider's batched INSERT/UPDATE/DELETE |
