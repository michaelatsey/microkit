# Rule: Specifications — MicroKit.Persistence

## `Specification<T>` lives in MicroKit.Domain

A specification is a domain predicate: it overrides `ToExpression()` and nothing else. It knows
nothing of EF Core, includes, ordering or pagination.

```csharp
// ✅ MicroKit.Domain.Specifications.Specification<T>
public sealed class OpenOrdersSpec : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() => order => !order.IsClosed;
}
```

`IsSatisfiedBy` (compiled once) evaluates it in memory — the in-memory doubles use it.
`And`/`Or`/`Not` compose specifications but return `ISpecification<T>`, and `QueryOptions<T>`
takes a `Specification<T>`: to query a composition, wrap it in a named `Specification<T>` whose
`ToExpression()` returns the composed expression.

## `QueryOptions<T>` — loading strategy (core, ADR-002)

```csharp
var opts = new QueryOptions<Order>(new OpenOrdersSpec())     // WHAT
    .WithIncludes(q => q.Include(o => o.Lines))              // HOW: eager loading
    .WithOrderByDescending(o => o.CreatedAt)                 //      ordering (Specifications package)
    .WithPagination(page: 1, pageSize: 20)                   //      pagination (validated: page ≥ 1, size ≥ 1)
    .AsSplitQuery()                                          //      split collections
    .WithDeleted();                                          //      soft-deleted aggregates too (ADR-012)
```

The read side never tracks: there is no tracking option (ADR-009).

## `ISpecificationEvaluator` lives in core

```csharp
public interface ISpecificationEvaluator
{
    IQueryable<T> GetQuery<T>(IQueryable<T> inputQuery, QueryOptions<T> opts) where T : class, IAggregateRoot;
}
```

It takes `IQueryable<T>`, so it stays out of Abstractions. `EfSpecificationEvaluator` implements
it (criteria → includes → split → ordering → pagination) and `AddEntityFrameworkCore` registers it.

## `MicroKit.Persistence.Specifications` package

```csharp
opts.WithOrderBy(o => o.CreatedAt)
    .WithThenBy(o => o.Number)            // also WithOrderByDescending, WithThenByDescending
opts.WithSpec(new OpenOrdersSpec())       // replace the specification (null clears it)
```

`WithThenBy*` on options without ordering starts the ordering.

### Why `With-` and not `OrderBy`

`QueryOptions<T>` has a delegate property `OrderBy`. Instance members win over extension
methods, so `opts.OrderBy(lambda)` is a delegate invocation on the property and does not compile.
That is why core's delegate-based extension is `WithOrdering(Func<IQueryable<T>, IOrderedQueryable<T>>)`
(it was `OrderBy`, reachable only as a static call). Use `WithOrderBy…` for a key selector,
`WithOrdering(q => …)` for a custom ordering delegate.

## Rules

```
✅ Specification<T>: ToExpression() only
✅ QueryOptions<T> for includes, ordering, pagination, split queries (never tracking)
✅ ISpecificationEvaluator in core, EfSpecificationEvaluator in EntityFrameworkCore
❌ Include / OrderBy / Skip / Take inside a specification
❌ A specification that depends on EF Core or DbContext
❌ IQueryable in any Abstractions type
```
