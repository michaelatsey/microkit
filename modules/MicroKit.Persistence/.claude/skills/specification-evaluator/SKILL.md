---
name: specification-evaluator
description: How ISpecificationEvaluator works in MicroKit.Persistence — applies QueryOptions (specification, includes, split query, ordering, pagination) to an IQueryable<T>, and how EfReadRepository uses it with tracking and PaginationLimits. Use when implementing or debugging query composition, decorating the evaluator, or tracing a specification to SQL.
---

# Skill: Specification Evaluator

## Contract (core, not Abstractions — it takes `IQueryable<T>`)

```csharp
public interface ISpecificationEvaluator
{
    IQueryable<T> GetQuery<T>(IQueryable<T> inputQuery, QueryOptions<T> opts)
        where T : class, IAggregateRoot;
}
```

## `EfSpecificationEvaluator` — the order is fixed

```csharp
if (opts.Specification is { } spec) query = query.Where(spec.ToExpression()); // 1. criteria
if (opts.Includes is not null)       query = opts.Includes(query);           // 2. eager loading
if (opts.AsSplitQueryEnabled)        query = query.AsSplitQuery();           // 3. split hint
if (opts.OrderBy is not null)        query = opts.OrderBy(query);            // 4. ordering
if (opts.Pagination is { } p)        query = query.Skip(p.Skip).Take(p.PageSize); // 5. pagination
```

`PaginationOptions` validates `page ≥ 1`, `pageSize ≥ 1` and computes `Skip` without overflow.
`AddEntityFrameworkCore` registers the evaluator as a singleton (`TryAdd`, so a prior
registration wins).

## How `EfReadRepository` calls it

- Base query: `Set<T>().AsNoTracking()`, always; `AsNoTracking()` is applied again after the
  evaluator for list queries, so a delegate cannot make the read side track.
- `ListAsync`: rejects a page size above `PaginationLimits.MaxPageSize`, then evaluates.
- `ListPagedAsync`: applies `PaginationLimits.ForPagedQuery` (default page when none), runs a
  count without includes/ordering/pagination, then the page query.
- `AnyAsync` / `CountAsync`: evaluate with includes, ordering, pagination and split removed.

A typed read repository passes its own base query (for instance a projection source) through
the same evaluator.

## Decorating

To add logic every query needs, decorate `ISpecificationEvaluator` and call the inner one; never
drop the criteria step. Never bypass the model's query filters: the parameterless
`IgnoreQueryFilters()` disables all of them, a tenant filter included, and `IlCallScanTests`
fails on any library call to it. Only the named overload `IgnoreQueryFilters(names)` may be used.

## Debugging

- `query.ToQueryString()` on the evaluated query shows the SQL.
- An unordered paginated query gives unstable pages — set `WithOrderBy…`.
- A composed `ISpecification<T>` (`And`/`Or`/`Not`) is not a `Specification<T>`; wrap it before
  passing it to `QueryOptions` (see `.claude/rules/specifications.md`).
