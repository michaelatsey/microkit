# Rule: Specifications — MicroKit.Persistence

## Specification<T> Lives in MicroKit.Domain

> `Specification<T>` is a **domain concept** — a pure, composable predicate over a domain entity.
> It lives in `MicroKit.Domain` and contains **criteria only** (the `Expression<Func<T, bool>>`).
> It has no knowledge of EF Core, IQueryable, Include, or pagination.

```csharp
// ✅ In MicroKit.Domain — pure criteria
public sealed class ActiveUserSpec : Specification<User>
{
    public ActiveUserSpec() => AddCriteria(u => u.IsActive && !u.IsDeleted);
}

// ❌ Include in a Specification — this is a loading concern, not a domain concern
public sealed class UserWithRolesSpec : Specification<User>
{
    public UserWithRolesSpec()
    {
        AddCriteria(u => u.IsActive);
        AddInclude(u => u.Roles); // ❌ belongs in QueryOptions, not Spec
    }
}
```

## QueryOptions<T> — Wraps Specification with Loading Strategy

`QueryOptions<T>` lives in `MicroKit.Persistence` (Core) and separates **WHAT** from **HOW**:

```csharp
// WHAT  → Specification (domain layer)
// HOW   → QueryOptions (persistence layer — include strategy, tracking, pagination)

var opts = new QueryOptions<User>(new ActiveUserSpec())
    .WithIncludes(q => q.Include(u => u.Roles).ThenInclude(r => r.Permissions))
    .WithPagination(page: 1, pageSize: 20)
    .AsNoTracking()   // redundant on IReadRepository but explicit on IRepository read paths
    .AsSplitQuery();  // avoids Cartesian explosion for multi-collection includes
```

## ISpecificationEvaluator Lives in Core, Not Abstractions

```csharp
// ✅ In MicroKit.Persistence (Core) — applies QueryOptions to an IQueryable<T>
public interface ISpecificationEvaluator
{
    IQueryable<T> GetQuery<T>(IQueryable<T> inputQuery, QueryOptions<T> opts)
        where T : class, IAggregateRoot;
}

// ✅ Implemented in EntityFrameworkCore project
public sealed class EfSpecificationEvaluator : ISpecificationEvaluator { ... }

// ❌ ISpecificationEvaluator in Abstractions — it references IQueryable, which would
// force EntityFrameworkCore concerns into the abstraction layer
```

## MicroKit.Persistence.Specifications Package

The `Specifications` package provides QueryOptions extensions and composition helpers:

```csharp
// ✅ Ordering extensions (in Specifications package) — use With- prefix
opts.WithOrderBy(u => u.CreatedAt)
opts.WithOrderByDescending(u => u.Email)
opts.WithThenBy(u => u.LastName)
opts.WithThenByDescending(u => u.CreatedAt)

// ✅ Specification-swap extension (in Specifications package)
opts.WithSpec(new ActiveUserSpec())

// ✅ Spec composition helpers — built into Specification<T> in MicroKit.Domain
new ActiveUserSpec().And(new UserByEmailSpec(email))
new ActiveUserSpec().Or(new AdminUserSpec())
new ActiveUserSpec().Not()
```

> **Why `With-` prefix for ordering?**  
> `QueryOptions<T>` exposes a delegate property named `OrderBy`
> (`Func<IQueryable<T>, IOrderedQueryable<T>>?`). When both the Core and Specifications
> namespaces are in scope, C# resolves `opts.OrderBy(lambda)` as a **delegate invocation
> on the property**, not as the extension method — because instance member access takes
> precedence over extension method lookup. The call then fails to compile because
> `lambda` cannot be converted to `IQueryable<T>` (the property's parameter type).
> The `With-` prefix (`WithOrderBy`, `WithOrderByDescending`, `WithThenBy`,
> `WithThenByDescending`) sidesteps this collision entirely and is consistent with
> Core's existing builder convention: `WithIncludes`, `WithPagination`, `WithTracking`.

## Rules

```
✅ Specification<T>.Criteria only — Expression<Func<T, bool>>
✅ QueryOptions<T> for Include, pagination, tracking hints
✅ ISpecificationEvaluator in Core — not Abstractions
✅ MicroKit.Persistence.Specifications for composition helpers
❌ Include() in any Specification subclass
❌ OrderBy/Skip/Take in Specification
❌ IQueryable in any Abstractions type
❌ Specification with DbContext or EF Core dependency
```
