# MicroKit.Caching.Distributed.Autofac

Autofac adapter for `MicroKit.Caching.Distributed`. Provides a `RegisterMicroKitDistributedCache()` extension on `ContainerBuilder` that registers `DistributedCacheService` as the singleton `ICacheService`. Uses `IfNotRegistered` to prevent double-registration when the extension is called from multiple modules.

## When to use

Use this package instead of `MicroKit.Caching.Distributed`'s Microsoft DI extension when your application container is Autofac. Both packages register the same `DistributedCacheService` — the difference is only in the container API used.

## Installation

```
dotnet add package MicroKit.Caching.Distributed.Autofac
```

## Key types

| Type | Description |
|---|---|
| `MicroKitDistributedCacheAutofacExtensions.RegisterMicroKitDistributedCache()` | Registers `DistributedCacheService` as singleton `ICacheService` via `IfNotRegistered`; safe to call multiple times |

## Usage

```csharp
// In an Autofac module or program setup
var builder = new ContainerBuilder();

// Provide the underlying IDistributedCache (e.g. Redis via Microsoft.Extensions.Caching.StackExchangeRedis)
builder.RegisterType<RedisDistributedCache>()
       .As<IDistributedCache>()
       .SingleInstance();

// Register the MicroKit distributed cache service
builder.RegisterMicroKitDistributedCache();

// The same call is made internally by MicroKit.Cqrs.MediatR.Caching's UseDistributedCache() extension
// so you generally don't need to call it manually when using that package.
```

This package is also called internally by `MicroKit.Cqrs.MediatR.Caching.CqrsMediatRCachingExtension.UseDistributedCache()` — if you use that extension, you do not need to call `RegisterMicroKitDistributedCache()` yourself.

## Dependencies

- `Autofac.Extensions.DependencyInjection`
- `MicroKit.Caching.Abstractions`
- `MicroKit.Caching.Distributed`
- `MicroKit.Abstractions`
