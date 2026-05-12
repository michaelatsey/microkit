# MicroKit.MultiTenancy.EFCoreStore

EF Core-backed implementation of `ITenantStore` and `ITenantRegistry`. `EFCoreTenantStore<TContext>` reads tenant records from the application's `DbContext` and caches results via the registered `ITenantCache`, reducing database hits for repeated tenant lookups within a request window.

## When to use

Use this when tenant metadata is stored in a relational database managed by EF Core. Pair with `MicroKit.MultiTenancy.Redis` to provide the caching layer, or implement `ITenantCache` yourself.

For tenants managed by a separate microservice, implement `ITenantStore` with an HTTP client instead.

## Installation

```
dotnet add package MicroKit.MultiTenancy.EFCoreStore
```

## Key types

| Type | Description |
|---|---|
| `EFCoreTenantStore<TContext>` | Implements both `ITenantStore` and `ITenantRegistry`; reads tenant entities from `TContext`, caches results via `ITenantCache` |
| `TenantStoreExtensions.WithDatabaseStore<TContext>()` | Registers the store and wires `ITenantStore` and `ITenantRegistry` via `MicroKitMultiTenantBuilder`; accepts an optional service override callback |

## Usage

```csharp
services
    .AddMicroKitMultiTenancy()
    .WithRedisCache()                        // from MicroKit.MultiTenancy.Redis
    .WithDatabaseStore<AppDbContext>(opts =>
    {
        opts.CacheExpiration = TimeSpan.FromMinutes(30);
    });
```

The `WithDatabaseStore<TContext>()` method:
1. Removes any previously registered `ITenantStore` and `ITenantRegistry` bindings.
2. Registers `EFCoreTenantStore<TContext>` as scoped.
3. Binds `ITenantStore` and `ITenantRegistry` to the same instance.

Pass a second `services` callback to override specific registrations after the defaults are applied (e.g. to supply a custom `ITenantRegistry` backed by a different source).

## Dependencies

- `Microsoft.EntityFrameworkCore`
- `MicroKit.MultiTenancy.Abstractions`
- `MicroKit.MultiTenancy`
- `MicroKit.Abstractions`
