# Standard: QueryOptions Pattern

**The canonical QueryOptions contract and its design rationale.**

---

## Core Principle (ADR-002)

`Specification<T>` (from MicroKit.Domain) answers **WHAT** to query:
```csharp
public sealed class ActiveUserSpec : Specification<User>
{
    public ActiveUserSpec() => AddCriteria(u => u.IsActive && !u.IsDeleted);
}
```

`QueryOptions<T>` (from MicroKit.Persistence Core) answers **HOW** to execute it:
```csharp
var opts = new QueryOptions<User>(new ActiveUserSpec())
    .WithIncludes(q => q.Include(u => u.Roles))
    .WithPagination(page: 1, pageSize: 20)
    .AsNoTracking()
    .AsSplitQuery();
```

This separation ensures that domain specifications remain EF-Core-free and testable with a simple
`.Criteria.Compile()(entity)` call, while loading strategy (includes, tracking, pagination) is
expressed in the application layer.

---

## QueryOptions<T> Shape

```csharp
namespace MicroKit.Persistence;

/// <summary>
/// Encapsulates the loading strategy for a read query: specification (WHAT),
/// includes (HOW to load), pagination, tracking, and split-query hints.
/// </summary>
public sealed record QueryOptions<T>
    where T : class, IAggregateRoot
{
    public Specification<T>? Specification { get; init; }
    public Func<IQueryable<T>, IQueryable<T>>? Includes { get; init; }
    public PaginationOptions? Pagination { get; init; }
    public bool AsNoTrackingEnabled { get; init; } = true;  // default: no tracking
    public bool AsSplitQueryEnabled { get; init; }
    public Func<IQueryable<T>, IOrderedQueryable<T>>? OrderBy { get; init; }
    public bool IncludeDeleted { get; init; }  // for soft-delete support

    public QueryOptions(Specification<T>? specification = null)
        => Specification = specification;
}
```

---

## Builder Methods (fluent API)

```csharp
// Include navigations
opts.WithIncludes(q => q.Include(u => u.Roles).ThenInclude(r => r.Permissions))

// Pagination
opts.WithPagination(page: 1, pageSize: 20)
opts.WithPagination(new PaginationOptions(Page: 1, PageSize: 20))

// Tracking hint (default is NoTracking — explicit call for clarity on write paths)
opts.AsNoTracking()        // explicit
opts.WithTracking()        // opt-in for write paths that need change tracking

// Split query
opts.AsSplitQuery()

// Ordering
opts.OrderBy(q => q.OrderByDescending(u => u.CreatedAt))

// Soft deletes
opts.IncludeSoftDeleted()
```

---

## PaginationOptions

```csharp
public sealed record PaginationOptions(int Page, int PageSize)
{
    public int Skip => (Page - 1) * PageSize;
}
```

---

## EfSpecificationEvaluator Application Order

The evaluator applies QueryOptions in this order to prevent invalid EF Core query states:

1. Specification criteria (`.Where(...)`)
2. Includes (`.Include(...).ThenInclude(...)`)
3. Split query hint (`.AsSplitQuery()`)
4. Ordering (`.OrderBy(...)`)
5. Pagination (`.Skip(...).Take(...)`) — must come after ordering

Changing this order may produce SQL that EF Core cannot translate or that returns inconsistent results.

---

## Where QueryOptions Lives

| Type | Project | Namespace |
|------|---------|-----------|
| `QueryOptions<T>` | `MicroKit.Persistence` (Core) | `MicroKit.Persistence` |
| `PaginationOptions` | `MicroKit.Persistence` (Core) | `MicroKit.Persistence` |
| `ISpecificationEvaluator` | `MicroKit.Persistence` (Core) | `MicroKit.Persistence` |
| `EfSpecificationEvaluator` | `MicroKit.Persistence.EntityFrameworkCore` | `MicroKit.Persistence.EntityFrameworkCore` |
| QueryOptions extensions | `MicroKit.Persistence.Specifications` | `MicroKit.Persistence.Specifications` |
