# Template: Read Repository

Use this template for scaffolding a new `I{Entity}ReadRepository` + `Ef{Entity}ReadRepository` pair.

---

## Interface — MicroKit.Persistence.Abstractions

```csharp
namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Read-side repository for <see cref="{Entity}"/> aggregates.
/// Never mutates state; always queries without change tracking.
/// </summary>
public interface I{Entity}ReadRepository : IReadRepository<{Entity}>
{
    // Add read-only query methods here. No mutations. No CommitAsync.

    /// <summary>
    /// Returns a summary projection of <see cref="{Entity}"/> with the given identifier.
    /// </summary>
    ValueTask<{Entity}SummaryDto?> GetSummaryAsync({EntityId} id, CancellationToken ct = default);
}
```

---

## EF Core Implementation — MicroKit.Persistence.EntityFrameworkCore

```csharp
namespace MicroKit.Persistence.EntityFrameworkCore;

/// <inheritdoc cref="I{Entity}ReadRepository"/>
public sealed class Ef{Entity}ReadRepository(
    {AppDbContext} ctx,
    ISpecificationEvaluator evaluator)
    : I{Entity}ReadRepository
{
    public async ValueTask<{Entity}?> FindAsync({EntityId} id, CancellationToken ct = default)
        => await ctx.{Entities}
            .AsNoTracking()                          // ← mandatory
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            .ConfigureAwait(false);

    public async ValueTask<IReadOnlyList<{Entity}>> ListAsync(
        QueryOptions<{Entity}> opts,
        CancellationToken ct = default)
    {
        var query = evaluator.GetQuery(
            ctx.{Entities}.AsNoTracking(),           // ← mandatory
            opts);
        return await query.ToListAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<IPagedResult<{Entity}>> ListPagedAsync(
        QueryOptions<{Entity}> opts,
        CancellationToken ct = default)
    {
        var baseQuery = evaluator.GetQuery(ctx.{Entities}.AsNoTracking(), opts);
        var total = await baseQuery.CountAsync(ct).ConfigureAwait(false);
        var items = await baseQuery
            .Skip(opts.Pagination!.Skip)
            .Take(opts.Pagination!.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return new PagedResult<{Entity}>(items, total, opts.Pagination.Page, opts.Pagination.PageSize);
    }

    public async ValueTask<bool> AnyAsync(QueryOptions<{Entity}> opts, CancellationToken ct = default)
    {
        var query = ctx.{Entities}.AsNoTracking();
        if (opts.Specification?.Criteria is { } criteria)
            query = query.Where(criteria);
        return await query.AnyAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<int> CountAsync(QueryOptions<{Entity}> opts, CancellationToken ct = default)
    {
        var query = ctx.{Entities}.AsNoTracking();
        if (opts.Specification?.Criteria is { } criteria)
            query = query.Where(criteria);
        return await query.CountAsync(ct).ConfigureAwait(false);
    }

    // Entity-specific methods:

    public async ValueTask<{Entity}SummaryDto?> GetSummaryAsync({EntityId} id, CancellationToken ct)
        => await ctx.{Entities}
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select({Entity}SummaryDto.Projection)   // ← server-side projection
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
}
```

---

## DI Registration

```csharp
services.AddScoped<I{Entity}ReadRepository, Ef{Entity}ReadRepository>();
```
