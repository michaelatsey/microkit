# MicroKit.Cqrs.MediatR.Caching

MediatR pipeline behaviors for automatic query caching and command-driven cache invalidation. Integrates with `MicroKit.Caching.Distributed` through `ICacheService`. Behaviors activate on requests that implement `ICacheableRequest` (queries) or `ICacheInvalidatorRequest<,>` (commands).

## When to use

Add this package when you want the pipeline to handle caching transparently — handlers remain unaware of caching. Pair with `MicroKit.Cqrs.MediatR.Autofac` for pipeline registration.

## Installation

```
dotnet add package MicroKit.Cqrs.MediatR.Caching
```

## Key types

| Type | Description |
|---|---|
| `CachingBehavior<TRequest, TResponse>` | Intercepts `ICacheableRequest` queries; returns cached response on hit, caches the handler result on miss; respects `BypassCache`, `SlidingExpiration`, and `CacheDuration` |
| `CacheInvalidationBehavior<TRequest, TResponse>` | Intercepts `ICacheInvalidatorRequest<,>` commands; after successful execution removes the keys returned by `GetCacheKeys()` |
| `CqrsMediatRCachingExtension.UseDistributedCache()` | Registers distributed cache services into the Autofac container via `CqrsMediatRBuilder` |

## Usage

```csharp
// 1. Mark a query as cacheable
public record GetProductQuery(Guid ProductId)
    : IQuery<ProductDto>, IRequest<ProductDto>, ICacheableRequest
{
    public string CacheKey => $"product:{ProductId}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(10);
    public CacheRequestOptions? Options => null;
}

// 2. Mark a command to invalidate cache on success
public record UpdateProductCommand(Guid ProductId, string Name)
    : ICommand, IRequest<Unit>,
      ICacheInvalidatorRequest<UpdateProductCommand, Unit>
{
    public IEnumerable<string> GetCacheKeys(UpdateProductCommand request, Unit response)
        => [$"product:{request.ProductId}"];
}

// 3. Register in Autofac
builder
    .AddMicroKitCqrs(opts => opts.RegisterAssembly(Assembly.GetExecutingAssembly()))
    .UseMediatRModule(mediatR =>
    {
        mediatR.AddPipeline<CachingBehavior<,>>(order: 10);
        mediatR.AddPipeline<CacheInvalidationBehavior<,>>(order: 20);
        mediatR.UseDistributedCache();
    });
```

## Dependencies

- `MicroKit.Cqrs.Abstractions`
- `MicroKit.Cqrs.MediatR.Abstractions`
- `MicroKit.Cqrs.MediatR.Autofac`
- `MicroKit.Caching`
- `MicroKit.Caching.Distributed.Autofac`
