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
| `CommitAsync` | `<summary>` + `<param name="ct">` + `<exception cref="PersistenceException">` |
| QueryOptions | `<summary>` + `<param>` on record parameters |
| IPagedResult | `<summary>` + `<param>` on all members |
| EF implementation `override` | `<inheritdoc/>` |

## Repository Contract Documentation Pattern

```csharp
/// <summary>
/// Write-side repository for <typeparamref name="TAggregate"/> aggregates.
/// Exposes the Unit of Work boundary via <see cref="CommitAsync"/>.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
public interface IRepository<TAggregate>
    where TAggregate : IAggregateRoot
{
    /// <summary>
    /// Finds an aggregate by its primary key.
    /// </summary>
    /// <returns>The aggregate, or <see langword="null"/> if not found.</returns>
    ValueTask<TAggregate?> FindAsync(/* id */, CancellationToken ct = default);

    /// <summary>
    /// Commits all pending changes to the underlying store.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <exception cref="PersistenceException">Thrown when the underlying provider fails to commit.</exception>
    ValueTask CommitAsync(CancellationToken ct = default);
}
```

## Style

- `<summary>` uses imperative mood: "Finds an aggregate." not "This method finds..."
- Cross-reference related contracts with `<see cref=""/>`
- Document why `CommitAsync` instead of `SaveChangesAsync` where it helps consumers understand
- EF implementation overrides use `<inheritdoc/>` exclusively — no duplication
