# Template: Write Repository

Use this template for scaffolding a new `I{Entity}Repository` + `Ef{Entity}Repository` pair.

---

## Interface — MicroKit.Persistence.Abstractions

```csharp
namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Write-side repository for <see cref="{Entity}"/> aggregates.
/// </summary>
public interface I{Entity}Repository : IRepository<{Entity}>
{
    // Add entity-specific finder methods here:

    /// <summary>
    /// Finds a <see cref="{Entity}"/> by <paramref name="email"/>.
    /// </summary>
    /// <returns>The <see cref="{Entity}"/>, or <see langword="null"/> if not found.</returns>
    ValueTask<{Entity}?> FindBy{Property}Async({PropertyType} {property}, CancellationToken ct = default);
}
```

---

## EF Core Implementation — MicroKit.Persistence.EntityFrameworkCore

```csharp
namespace MicroKit.Persistence.EntityFrameworkCore;

/// <inheritdoc cref="I{Entity}Repository"/>
public sealed class Ef{Entity}Repository(
    {AppDbContext} ctx,
    IUnitOfWork uow)
    : I{Entity}Repository
{
    public async ValueTask<{Entity}?> FindAsync({EntityId} id, CancellationToken ct = default)
        => await ctx.{Entities}.FindAsync([id.Value], ct).ConfigureAwait(false);

    public async ValueTask AddAsync({Entity} aggregate, CancellationToken ct = default)
        => await ctx.{Entities}.AddAsync(aggregate, ct).ConfigureAwait(false);

    public ValueTask UpdateAsync({Entity} aggregate, CancellationToken ct = default)
    {
        ctx.{Entities}.Update(aggregate);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync({Entity} aggregate, CancellationToken ct = default)
    {
        ctx.{Entities}.Remove(aggregate);
        return ValueTask.CompletedTask;
    }

    public async ValueTask CommitAsync(CancellationToken ct = default)
        => await uow.CommitAsync(ct).ConfigureAwait(false);

    // Entity-specific methods:

    public async ValueTask<{Entity}?> FindBy{Property}Async({PropertyType} {property}, CancellationToken ct = default)
        => await ctx.{Entities}
            .FirstOrDefaultAsync(e => e.{Property} == {property}, ct)
            .ConfigureAwait(false);
}
```

---

## DI Registration — ServiceCollectionExtensions

```csharp
services.AddScoped<I{Entity}Repository, Ef{Entity}Repository>();
```
