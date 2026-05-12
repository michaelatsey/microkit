# MicroKit.Caching.Abstractions

Cache service contract with no external dependencies. Defines `ICacheService` and `CacheOptions`. Reference this in any package that needs to read or write cached data without binding to a specific cache provider.

## When to use

Reference `MicroKit.Caching.Abstractions` in application and domain packages. Register `MicroKit.Caching.Distributed` or `MicroKit.Caching.Distributed.Autofac` at the composition root to provide the implementation.

## Installation

```
dotnet add package MicroKit.Caching.Abstractions
```

## Key types

| Type | Description |
|---|---|
| `ICacheService` | `GetAsync<T>`, `SetAsync<T>`, `RemoveAsync` |
| `CacheOptions` | Per-call configuration: `Duration`, `BypassCache`, `SlidingExpiration` |

### `ICacheService`

```csharp
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, CacheOptions? options = null, CancellationToken cancellationToken = default)
        where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
```

## Usage

```csharp
// Application service using the abstraction
public class ProductService(ICacheService cache)
{
    private const string Prefix = "product:";

    public async Task<ProductDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var key = $"{Prefix}{id}";
        var cached = await cache.GetAsync<ProductDto>(key, ct);
        if (cached is not null) return cached;

        var product = await _repository.FindByIdAsync(id, ct);
        if (product is null) return null;

        await cache.SetAsync(key, product.ToDto(), new CacheOptions
        {
            Duration = TimeSpan.FromMinutes(15),
            SlidingExpiration = false
        }, ct);

        return product.ToDto();
    }

    public Task InvalidateAsync(Guid id, CancellationToken ct)
        => cache.RemoveAsync($"{Prefix}{id}", ct);
}
```

## Dependencies

None.
