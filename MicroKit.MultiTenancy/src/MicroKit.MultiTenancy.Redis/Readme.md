# MicroKit.MultiTenancy.Redis

Two-level `ITenantCache` implementation. `RedisTenantCache` uses an in-process `IMemoryCache` as L1 and a Redis-backed `IDistributedCache` as L2. L1 absorbs repeated lookups for the same tenant within a short window; L2 shares tenant data across multiple application instances.

## When to use

Use this alongside any `ITenantStore` implementation to reduce per-request store lookups. Register `AddStackExchangeRedisCache` (or another `IDistributedCache` provider) separately before calling `WithRedisCache()`.

## Installation

```
dotnet add package MicroKit.MultiTenancy.Redis
```

## Key types

| Type | Description |
|---|---|
| `RedisTenantCache` | `ITenantCache` with L1 `IMemoryCache` + L2 `IDistributedCache` |
| `RedisTenantCacheOptions.L1Ttl` | In-process memory TTL (default: 5 minutes) |
| `TenantCacheExtensions.WithRedisCache()` | Registers `RedisTenantCache` as `ITenantCache` via `MicroKitMultiTenantBuilder` |

## Usage

```csharp
// Register Redis and the tenant cache
services.AddStackExchangeRedisCache(opts =>
    opts.Configuration = "redis.internal:6379");

services.AddMemoryCache();

services
    .AddMicroKitMultiTenancy()
    .WithRedisCache(opts =>
    {
        opts.L1Ttl = TimeSpan.FromMinutes(2); // in-process TTL
    })
    .WithDatabaseStore<AppDbContext>();
```

Cache operations:
- **Get**: check L1 (`IMemoryCache`) first; on miss, check L2 (Redis); on L2 hit, backfill L1.
- **Set**: write to L2 with the caller-provided absolute TTL, then write to L1 with `L1Ttl`.
- **Remove**: remove from L2, then remove from L1.

## Dependencies

- `Microsoft.Extensions.Caching.StackExchangeRedis`
- `MicroKit.MultiTenancy.Abstractions`
- `MicroKit.MultiTenancy`
