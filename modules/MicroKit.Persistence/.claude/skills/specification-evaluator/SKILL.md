---
name: specification-evaluator
description: How ISpecificationEvaluator works in MicroKit.Persistence — applies QueryOptions (spec, includes, pagination, tracking, split queries) to an IQueryable<T>. Use when implementing or debugging query composition, evaluator customization, or spec-to-SQL translation.
---

# Skill: Specification Evaluator

How `ISpecificationEvaluator` applies `QueryOptions<T>` to `IQueryable<T>`.

## Interface (lives in Core, not Abstractions)

```csharp
public interface ISpecificationEvaluator
{
    IQueryable<T> GetQuery<T>(IQueryable<T> inputQuery, QueryOptions<T> opts)
        where T : class, IAggregateRoot;
}
```

## EF Core Implementation

```csharp
public sealed class EfSpecificationEvaluator : ISpecificationEvaluator
{
    public IQueryable<T> GetQuery<T>(IQueryable<T> inputQuery, QueryOptions<T> opts)
        where T : class, IAggregateRoot
    {
        var query = inputQuery;

        // 1. Apply spec criteria
        if (opts.Specification?.Criteria is { } criteria)
            query = query.Where(criteria);

        // 2. Apply includes (eager loading)
        if (opts.Includes is not null)
            query = opts.Includes(query);

        // 3. Apply split query hint
        if (opts.AsSplitQueryEnabled)
            query = query.AsSplitQuery();

        // 4. Apply ordering
        if (opts.OrderBy is not null)
            query = opts.OrderBy(query);

        // 5. Apply pagination (always after ordering)
        if (opts.Pagination is { } p)
            query = query.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize);

        return query;
    }
}
```

## Injection Pattern

The evaluator is injected into EF repositories:

```csharp
public sealed class EfUserReadRepository(AppDbContext ctx, ISpecificationEvaluator evaluator)
    : IUserReadRepository
{
    public async ValueTask<IReadOnlyList<User>> ListAsync(QueryOptions<User> opts, CancellationToken ct)
    {
        var query = evaluator.GetQuery(ctx.Users.AsNoTracking(), opts);
        return await query.ToListAsync(ct).ConfigureAwait(false);
    }
}
```

## Custom Evaluator Extensions

Wrap `ISpecificationEvaluator` to add query logic every repository query needs (decorate the
registered `EfSpecificationEvaluator`; never replace the criteria step).

Never bypass the model's query filters from library code: the parameterless
`IgnoreQueryFilters()` disables every filter, a tenant filter included, and
`IlCallScanTests` fails the build on any library call to it (#196 D1). The module owns no soft
delete; #197 reintroduces `IncludeDeleted` on a *named* filter (`IgnoreQueryFilters(["<name>"])`).
