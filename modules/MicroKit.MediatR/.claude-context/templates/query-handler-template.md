# Template: Query Handler

Code template for a CQRS query + handler. Used by `/new-handler --type query` (and `--type stream`).
Replace all `{Placeholder}` values.

---

## File: `Get{Entity}[By{Discriminant}]Query.cs`

```csharp
namespace {App}.Application.{Feature};

/// <summary>Reads {entity} {by discriminant}.</summary>
/// <param name="{Param}">{Description}.</param>
public sealed record Get{Entity}By{Discriminant}Query({Inputs})
    : IQuery<Result<{Dto}>>{Markers}
{
    // If ICacheableQuery:
    /// <inheritdoc />
    public string CacheKey => $"{entity}:{ {Param} }";
    /// <inheritdoc />
    public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
}
```

## File: `Handlers/Get{Entity}By{Discriminant}Handler.cs`

```csharp
namespace {App}.Application.{Feature}.Handlers;

/// <summary>Handles <see cref="Get{Entity}By{Discriminant}Query"/> — read side only.</summary>
public sealed class Get{Entity}By{Discriminant}Handler(
    I{Entity}ReadRepository readRepo)
    : IQueryHandler<Get{Entity}By{Discriminant}Query, Result<{Dto}>>
{
    /// <inheritdoc />
    public async ValueTask<Result<{Dto}>> Handle(
        Get{Entity}By{Discriminant}Query query,
        CancellationToken ct = default)
    {
        var entity = await readRepo.FindAsync(query.{Param}, ct).ConfigureAwait(false);
        return entity is null
            ? Result.Failure<{Dto}>(new {Entity}NotFoundError(query.{Param}))
            : Result.Success(entity.ToDto());
        // Never persist here — queries do not mutate state.
    }
}
```

## Stream Variant

```csharp
public sealed record Get{Entities}FeedQuery(string Category) : IStreamQuery<{Dto}>;

public sealed class Get{Entities}FeedHandler(I{Entity}ReadRepository repo)
    : IStreamQueryHandler<Get{Entities}FeedQuery, {Dto}>
{
    public async IAsyncEnumerable<{Dto}> Handle(
        Get{Entities}FeedQuery query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in repo.StreamByCategoryAsync(query.Category, ct).ConfigureAwait(false))
            yield return item.ToDto();
    }
}
```

## DI Registration

```csharp
services.AddTransient<IQueryHandler<Get{Entity}By{Discriminant}Query, Result<{Dto}>>, Get{Entity}By{Discriminant}Handler>();
```

## Rules Applied

- `sealed record` query, `sealed class` handler + primary constructor
- Read-only repository — no write repo, no `SaveChanges`, no `DbContext` directly
- `ValueTask<Result<T>>` (or `IAsyncEnumerable<T>` for streams), `CancellationToken ct = default` last
- `[EnumeratorCancellation]` on the stream handler's token
- `ConfigureAwait(false)` on every await
- No domain events from a query handler
