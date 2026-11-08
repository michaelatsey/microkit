# Rule: Documentation — MicroKit.Persistence

## XML Documentation (src/ projects only)

All public types and members in `src/` projects require XML documentation. The repository
contracts in `MicroKit.Persistence.Abstractions` are a published API.

```xml
<GenerateDocumentationFile>true</GenerateDocumentationFile>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

A missing `<summary>` on a public member is a build error in Release configuration.

## Minimum Required Tags

| Member type | Required tags |
|-------------|--------------|
| Interface | `<summary>` |
| Repository method | `<summary>` + `<param>` + `<returns>` |
| Exception class | `<summary>` + `<param>` on constructors |
| `IUnitOfWork.CommitAsync` | `<summary>` + `<param name="ct">` + `<exception cref="PersistenceException">` |
| QueryOptions | `<summary>` + `<param>` on record parameters |
| IPagedResult | `<summary>` + `<param>` on all members |
| EF implementation `override` | `<inheritdoc/>` |

## Repository Contract Documentation Pattern

```csharp
/// <summary>
/// Write-side repository for <typeparamref name="TAggregate"/> aggregates. It never commits —
/// committing belongs to <see cref="IUnitOfWork"/>.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The aggregate's identifier type.</typeparam>
public interface IRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot
    where TId : IEntityId
{
    /// <summary>
    /// Loads the whole aggregate identified by <paramref name="id"/>, tracked.
    /// </summary>
    /// <param name="id">The aggregate's identifier.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The aggregate, or <see langword="null"/> if none has this identifier.</returns>
    ValueTask<TAggregate?> FindAsync(TId id, CancellationToken ct = default);
}
```

## Style

- `<summary>` uses imperative mood: "Finds an aggregate." not "This method finds..."
- Cross-reference related contracts with `<see cref=""/>`
- Document why `CommitAsync` instead of `SaveChangesAsync` where it helps consumers understand
- EF implementation overrides use `<inheritdoc/>` exclusively — no duplication
