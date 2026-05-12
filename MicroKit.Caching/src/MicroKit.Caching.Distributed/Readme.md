# MicroKit.Caching.Distributed

`ICacheService` implementation backed by `IDistributedCache`. `DistributedCacheService` serializes cache values with `System.Text.Json` and maps `CacheOptions` to `DistributedCacheEntryOptions`. Works with any `IDistributedCache` provider (Redis via `AddStackExchangeRedisCache`, in-memory via `AddDistributedMemoryCache`, SQL Server, etc.).

## When to use

Use `MicroKit.Caching.Distributed` when your DI container is Microsoft DI. Use `MicroKit.Caching.Distributed.Autofac` when using Autofac, as it provides the equivalent `ContainerBuilder` registration extension.

## Installation

```
dotnet add package MicroKit.Caching.Distributed
```

## Key types

| Type | Description |
|---|---|
| `DistributedCacheService` | `ICacheService` backed by `IDistributedCache`; JSON serialization with `System.Text.Json` |
| `DistributedCacheOptions.SerializerOptions` | `JsonSerializerOptions` used for cache serialization (default: property-name-case-insensitive) |
| `MicroKitCachingBuilderExtensions.AddMicroKitDistributedCache()` | Extension on `MicroKitBuilder` or `IServiceCollection`; guard against double registration |

## Usage

```csharp
// With Redis (recommended for production)
services.AddStackExchangeRedisCache(opts =>
    opts.Configuration = "redis.internal:6379,abortConnect=false");

services
    .AddMicroKit()
    .AddMicroKitDistributedCache(opts =>
    {
        opts.SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    });

// With in-memory distributed cache (development / single-node)
services.AddDistributedMemoryCache();

services
    .AddMicroKit()
    .AddMicroKitDistributedCache();
```

`AddMicroKitDistributedCache()` uses `TryAddSingleton` and checks for an existing `ICacheService` registration, so it is safe to call in multiple places without double-registering.

## Dependencies

- `MicroKit.Caching.Abstractions`
- `MicroKit.Abstractions`
- `Microsoft.Extensions.Caching.Abstractions`
- `Microsoft.Extensions.Options`
