# MicroKit.Caching

Distributed cache abstraction for .NET 10. Provider-agnostic `ICacheService` over `IDistributedCache`, with per-call bypass, sliding/absolute expiration control, configurable JSON serialization, and idempotent DI registration.

---

## What makes this production-grade

**Sliding vs. absolute expiration per call, not per configuration.** The `CacheOptions` record carries a `SlidingExpiration` bool alongside `Duration`. `DistributedCacheService` maps this to `DistributedCacheEntryOptions.SlidingExpiration` or `AbsoluteExpirationRelativeToNow` accordingly. Most distributed cache wrappers require a global configuration choice; this one lets each `SetAsync` call decide.

**`BypassCache` flag for per-call opt-out.** When `CacheOptions.BypassCache = true`, the caller signals that this specific operation should read from and write to the source regardless of what is in the cache. The `CachingBehavior` in `MicroKit.Cqrs.MediatR.Caching` checks this flag before touching the cache at all, without the handler needing any conditional logic.

**Configurable `JsonSerializerOptions` without changing the abstraction.** `DistributedCacheOptions.SerializerOptions` accepts any `JsonSerializerOptions` instance. The default is `PropertyNameCaseInsensitive = true`. Pass camelCase, snake_case, or polymorphic type handling via DI configuration — the `ICacheService` interface is unchanged.

**Idempotent registration.** `AddMicroKitDistributedCache` checks whether `ICacheService` is already registered before adding `DistributedCacheService`. Multiple modules calling this method during startup produce exactly one registered implementation. There is no double-registration or ordering requirement.

**Default TTL of 30 minutes applies only when no duration is specified.** When `CacheOptions.Duration` is null, `DistributedCacheService` uses 30 minutes as an absolute expiration. Pass an explicit `TimeSpan` to override it per call.

---

## Installation

```shell
# Contracts — ICacheService, CacheOptions — zero deps
dotnet add package MicroKit.Caching.Abstractions

# DistributedCacheService backed by IDistributedCache (Redis, SQL, memory)
dotnet add package MicroKit.Caching.Distributed

# Autofac registration extension (optional)
dotnet add package MicroKit.Caching.Distributed.Autofac
```

---

## Usage

### Inject ICacheService directly

```csharp
using MicroKit.Caching.Abstractions;

public sealed class ProductQueryService
{
    private readonly IReadRepository<Product> _products;
    private readonly ICacheService _cache;

    public ProductQueryService(IReadRepository<Product> products, ICacheService cache) =>
        (_products, _cache) = (products, cache);

    public async Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var key = $"product:{id}";

        var cached = await _cache.GetAsync<ProductDto>(key, ct);
        if (cached is not null) return cached;

        var product = await _products.FindByIdAsync(id, ct);
        if (product is null) return null;

        var dto = new ProductDto(product);

        await _cache.SetAsync(
            key,
            dto,
            new CacheOptions(duration: TimeSpan.FromMinutes(10)),   // absolute expiration
            ct);

        return dto;
    }

    public Task InvalidateAsync(Guid id, CancellationToken ct) =>
        _cache.RemoveAsync($"product:{id}", ct);
}
```

### CacheOptions combinations

```csharp
// Absolute expiration — entry removed after exactly 5 minutes
new CacheOptions(duration: TimeSpan.FromMinutes(5))

// Sliding expiration — resets on each access; removed after 5 minutes of inactivity
new CacheOptions(duration: TimeSpan.FromMinutes(5), slidingExpiration: true)

// Default TTL (30 minutes, absolute)
await _cache.SetAsync(key, value, options: null, ct);

// Bypass — read from source, skip read and write to cache
new CacheOptions(bypassCache: true)
```

### Pipeline-level caching via MicroKit.Cqrs.MediatR.Caching

For query-level caching without manual `ICacheService` calls in handlers, implement `ICacheableRequest` on the query. `CachingBehavior` handles the get/set lifecycle automatically. See [MicroKit.Cqrs](../MicroKit.Cqrs/docs/Readme.md) for details.

```csharp
public record GetProductByIdQuery(Guid ProductId)
    : IQuery<ProductDto?>, ICacheableRequest
{
    public string CacheKey     => $"product:{ProductId}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(10);
    public CacheRequestOptions? Options => null;
}
```

---

## Configuration

### Microsoft DI — with Redis

```csharp
using MicroKit.Caching.Distributed;

// Register the IDistributedCache provider first
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName  = "myapp:";
});

// Register DistributedCacheService as ICacheService
builder.Services
    .AddMicroKit()
    .AddMicroKitDistributedCache(options =>
    {
        options.SerializerOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy        = System.Text.Json.JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    });
```

### Microsoft DI — in-memory (development and tests)

```csharp
builder.Services.AddDistributedMemoryCache();

builder.Services
    .AddMicroKit()
    .AddMicroKitDistributedCache();   // default JsonSerializerOptions applied
```

`AddMicroKitDistributedCache` is idempotent: if `ICacheService` is already registered (e.g. by another module), the call is a no-op.

### Autofac

```csharp
using MicroKit.Caching.Distributed.Autofac;

containerBuilder.AddMicroKitDistributedCache(options =>
{
    options.SerializerOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };
});
```

### Registration via IServiceCollection directly

```csharp
using MicroKit.Caching.Distributed;

// Bypasses the MicroKitBuilder — useful in test projects
services.AddDistributedMemoryCache();
services.AddMicroKitDistributedCache();
```

---

## Key types

| Type | Package | Role |
|---|---|---|
| `ICacheService` | Abstractions | `GetAsync<T>`, `SetAsync<T>`, `RemoveAsync` |
| `CacheOptions` | Abstractions | `Duration`, `BypassCache`, `SlidingExpiration` per-call config |
| `DistributedCacheService` | Distributed | `IDistributedCache`-backed implementation with JSON serialization |
| `DistributedCacheOptions` | Distributed | `SerializerOptions: JsonSerializerOptions` — defaults to case-insensitive |

---

## Package dependency graph

```
MicroKit.Caching.Abstractions
    (no NuGet dependencies)

MicroKit.Caching.Distributed
    MicroKit.Caching.Abstractions
    MicroKit.Abstractions
    Microsoft.Extensions.Caching.Abstractions

MicroKit.Caching.Distributed.Autofac
    MicroKit.Caching.Distributed
    Autofac
```
