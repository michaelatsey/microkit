# MicroKit.Cqrs.Abstractions

CQRS contracts with no external dependencies. Defines the full command/query dispatch surface — interfaces for commands, queries, handlers, buses, and the optional caching extension points. Application and domain layers should depend on this package exclusively.

## When to use

Reference `MicroKit.Cqrs.Abstractions` in:
- Application layer (command/query definitions, handler interfaces)
- Domain layer (if commands are defined there)
- Any package that dispatches or handles commands and queries

Do not reference `MicroKit.Cqrs`, `MicroKit.Cqrs.MediatR`, or `MicroKit.Cqrs.MediatR.Autofac` from application or domain code. Those are infrastructure concerns.

## Installation

```
dotnet add package MicroKit.Cqrs.Abstractions
```

## Key types

| Type | Description |
|---|---|
| `ICommand` | Marker interface for fire-and-forget commands |
| `ICommand<TResponse>` | Marker interface for commands that return a value |
| `IQuery<TResponse>` | Marker interface for queries |
| `ICommandBus` | `SendAsync<TCommand>()` and `SendAsync<TResponse>(ICommand<TResponse>)` |
| `IQueryBus` | `AskAsync<TResponse>(IQuery<TResponse>)` |
| `ICommandHandler<TCommand>` | Handler for fire-and-forget commands |
| `ICommandHandler<TCommand, TResponse>` | Handler for commands with a return value |
| `IQueryHandler<TQuery, TResponse>` | Handler for queries |
| `ICacheableRequest` | Opt a query into automatic pipeline caching; exposes `CacheKey`, `CacheDuration`, `Options` |
| `ICacheInvalidatorRequest<TRequest, TResponse>` | Opt a command into automatic cache invalidation; exposes `GetCacheKeys()` |
| `ICacheKeyService` | Builds fully-qualified cache keys from a custom key segment |
| `CacheRequestOptions` | Per-request cache flags: `BypassCache`, `SlidingExpiration` |

## Usage

```csharp
// Command definition
public record CreateOrderCommand(Guid CustomerId, List<OrderLineItem> Items)
    : ICommand<Guid>;

// Handler definition
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateOrderCommand command, CancellationToken ct)
    {
        // business logic
        return orderId;
    }
}

// Cacheable query
public record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>, ICacheableRequest
{
    public string CacheKey => $"order:{OrderId}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
    public CacheRequestOptions? Options => null;
}

// Dispatch from application service
public class OrderService(ICommandBus commands, IQueryBus queries)
{
    public Task<Guid> PlaceOrderAsync(CreateOrderCommand cmd, CancellationToken ct)
        => commands.SendAsync(cmd, ct);

    public Task<OrderDto> GetOrderAsync(Guid orderId, CancellationToken ct)
        => queries.AskAsync(new GetOrderQuery(orderId), ct);
}
```

## Dependencies

None.
