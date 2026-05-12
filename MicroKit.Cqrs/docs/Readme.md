# MicroKit.Cqrs

CQRS dispatch infrastructure for .NET 10. `ICommandBus` and `IQueryBus` are the only types your application layer ever touches — MediatR is an implementation detail confined to integration packages.

---

## What makes this production-grade

**MediatR decoupled by design, not convention.** `ICommand`, `IQuery`, `ICommandHandler`, and `IQueryHandler` are defined in `MicroKit.Cqrs.Abstractions` with zero third-party references. Your handlers implement these interfaces. `MediatRCommandBus` and `MediatRQueryBus` bridge them to MediatR internally. Swapping MediatR for a different dispatcher requires no changes to handlers or callers.

**Runtime type guard with actionable error messages.** `MediatRCommandBus.SendAsync` checks that the dispatched command also implements `IRequest<Unit>` or `IRequest<TResponse>` and throws `InvalidOperationException` with an exact message naming the command type and the concrete base class to inherit from. Dispatching a misconfigured command fails loudly at the call site, not deep in a pipeline behavior.

**Eligibility-gated cache writes.** `CachingBehavior` delegates the write decision to `ICacheEligibilityChecker.IsEligible(result)`. The default implementation skips null responses. Replace `DefaultCacheEligibilityChecker` in DI to suppress caching for failure `Result<T>` objects, empty collections, or any other application-specific non-cacheable response — without modifying the behavior.

**Externalized key construction.** `ICacheKeyService.BuildKey(customKey)` is the single point where a tenant ID prefix, environment namespace, or version token is appended. Inject a custom `ICacheKeyService` and every cached query in the application inherits the scoping automatically.

**Invalidation only on success.** `CacheInvalidationBehavior` executes the handler first, then calls `ICacheEligibilityChecker.IsEligible(response)` before touching the cache. If the command fails, stale cache entries are retained rather than removed — which is correct: a failed write should not produce a cache miss on the next read.

**IsEnabled guard before string formatting.** Both behaviors check `_logger.IsEnabled(LogLevel.Debug)` before constructing any log-argument strings. In production where Debug logging is disabled, the cache key string is never allocated.

**Fail-fast sequential validation.** `ValidationBehavior` runs `IValidator<TRequest>` instances one by one and throws `FluentValidation.ValidationException` on the first failure. Validators do not accumulate errors across each other — a broken precondition stops the pipeline immediately.

---

## Installation

```shell
# Abstractions — ICommand, IQuery, ICommandBus, IQueryBus, handlers, cache interfaces
dotnet add package MicroKit.Cqrs.Abstractions

# Autofac DI wiring, default cache key and eligibility services
dotnet add package MicroKit.Cqrs

# MediatR-backed bus implementations
dotnet add package MicroKit.Cqrs.MediatR

# Optional: LoggingBehavior, ValidationBehavior (FluentValidation)
dotnet add package MicroKit.Cqrs.MediatR.Behaviors

# Optional: CachingBehavior, CacheInvalidationBehavior
dotnet add package MicroKit.Cqrs.MediatR.Caching
```

---

## Usage

### Command with a typed response

```csharp
using MicroKit.Cqrs.Abstractions.Commands;
using MicroKit.Domain.Primitives;

// Marker: ICommand<TResponse> — no MediatR reference
public record CreateOrderCommand(Guid OrderId, string CustomerId) : ICommand<Result<Guid>>;

public sealed class CreateOrderHandler : ICommandHandler<CreateOrderCommand, Result<Guid>>
{
    private readonly IRepository<Order> _orders;
    private readonly IUnitOfWork _uow;

    public CreateOrderHandler(IRepository<Order> orders, IUnitOfWork uow) =>
        (_orders, _uow) = (orders, uow);

    public async Task<Result<Guid>> HandleAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Place(cmd.OrderId, cmd.CustomerId);
        await _orders.AddAsync(order, ct);
        await _uow.SaveChangesAsync(ct);
        return Result.Success(order.Id);
    }
}
```

### Fire-and-forget command

```csharp
// ICommand with no type parameter — no response
public record ArchiveOrderCommand(Guid OrderId) : ICommand;

public sealed class ArchiveOrderHandler : ICommandHandler<ArchiveOrderCommand>
{
    private readonly IRepository<Order> _orders;
    private readonly IUnitOfWork _uow;

    public ArchiveOrderHandler(IRepository<Order> orders, IUnitOfWork uow) =>
        (_orders, _uow) = (orders, uow);

    public async Task HandleAsync(ArchiveOrderCommand cmd, CancellationToken ct)
    {
        var order = await _orders.FindByIdAsync(cmd.OrderId, ct)
            ?? throw new NotFoundException($"Order {cmd.OrderId} not found.");
        order.Archive();
        _orders.Update(order);
        await _uow.SaveChangesAsync(ct);
    }
}

// Dispatch — no return value
await commandBus.SendAsync(new ArchiveOrderCommand(orderId), ct);
```

### Query

```csharp
using MicroKit.Cqrs.Abstractions.Queries;

public record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDto?>;

public sealed class GetOrderByIdHandler : IQueryHandler<GetOrderByIdQuery, OrderDto?>
{
    private readonly IReadRepository<Order> _orders;

    public GetOrderByIdHandler(IReadRepository<Order> orders) => _orders = orders;

    public async Task<OrderDto?> HandleAsync(GetOrderByIdQuery query, CancellationToken ct)
    {
        var order = await _orders.FindByIdAsync(query.OrderId, ct);
        return order is null ? null : new OrderDto(order);
    }
}

// Dispatch
var dto = await queryBus.AskAsync<OrderDto?>(new GetOrderByIdQuery(id), ct);
```

### Dispatch from an ASP.NET Core endpoint

```csharp
// ICommandBus and IQueryBus are injected — no MediatR, no ISender in the API layer
app.MapPost("/orders", async (CreateOrderCommand cmd, ICommandBus bus, CancellationToken ct) =>
{
    var result = await bus.SendAsync<Result<Guid>>(cmd, ct);
    return result.IsSuccess
        ? Results.Created($"/orders/{result.Value}", null)
        : Results.UnprocessableEntity(result.Error.Message);
});

app.MapGet("/orders/{id:guid}", async (Guid id, IQueryBus bus, CancellationToken ct) =>
{
    var dto = await bus.AskAsync<OrderDto?>(new GetOrderByIdQuery(id), ct);
    return dto is null ? Results.NotFound() : Results.Ok(dto);
});
```

### Cacheable query

Implement `ICacheableRequest` on any query. `CachingBehavior` intercepts it automatically.

```csharp
using MicroKit.Cqrs.Abstractions.Cache;

public record GetOrderByIdQuery(Guid OrderId)
    : IQuery<OrderDto?>, ICacheableRequest
{
    public string CacheKey => $"order:{OrderId}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(10);
    public CacheRequestOptions? Options => null;      // absolute expiration, no bypass
}
```

Override per-call:

```csharp
// Skip the cache for this specific call
public CacheRequestOptions? Options => new() { BypassCache = true };

// Use sliding expiration
public CacheRequestOptions? Options => new() { SlidingExpiration = true };
```

`CacheDuration` defaults to 15 minutes when null. `ICacheEligibilityChecker` (default: skip null) gates cache writes — replace it in DI to add custom rules.

### Cache invalidation on write

Implement `ICacheInvalidatorRequest<TRequest, TResponse>` on a command. After the handler succeeds, `CacheInvalidationBehavior` removes the returned keys.

```csharp
using MicroKit.Cqrs.Abstractions.Cache;

public record UpdateOrderCommand(Guid OrderId, string NewStatus)
    : ICommand<Result>, ICacheInvalidatorRequest<UpdateOrderCommand, Result>
{
    public IEnumerable<string> GetCacheKeys(UpdateOrderCommand request, Result response) =>
        [$"order:{request.OrderId}"];
}
```

The behavior runs the handler first. If the handler returns a non-eligible response (e.g. a failure `Result`), the cache keys are left intact.

### FluentValidation integration

Register validators as usual. `ValidationBehavior` picks them up from DI and runs them sequentially, throwing `FluentValidation.ValidationException` on the first invalid result.

```csharp
public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty().MaximumLength(100);
    }
}

// Registration (example with MediatR DI extension)
services.AddValidatorsFromAssemblyContaining<CreateOrderCommandValidator>();
```

---

## Configuration

### Autofac

```csharp
using Autofac;
using MicroKit.Cqrs;

// In your Autofac ContainerBuilder setup
containerBuilder.AddMicroKitCqrs();

// Register MediatR and the MediatR-backed buses from MicroKit.Cqrs.MediatR.Autofac
containerBuilder.AddCqrsMediatR(cfg =>
{
    cfg.RegisterHandlersFromAssemblyContaining<CreateOrderHandler>();
});
```

### Microsoft DI (without Autofac)

```csharp
using MicroKit.Cqrs.Abstractions.Commands;
using MicroKit.Cqrs.Abstractions.Queries;
using MicroKit.Cqrs.MediatR.Commands;
using MicroKit.Cqrs.MediatR.Queries;

services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<CreateOrderHandler>());

services.AddScoped<ICommandBus, MediatRCommandBus>();
services.AddScoped<IQueryBus, MediatRQueryBus>();

// Cache services (required by CachingBehavior)
services.AddScoped<ICacheKeyService, DefaultCacheKeyService>();
services.AddScoped<ICacheEligibilityChecker, DefaultCacheEligibilityChecker>();

// Behaviors — order matters in MediatR pipeline
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));
```

---

## Package dependency graph

```
MicroKit.Cqrs.Abstractions
    (no NuGet dependencies)

MicroKit.Cqrs
    MicroKit.Cqrs.Abstractions
    Autofac

MicroKit.Cqrs.MediatR.Abstractions
    MicroKit.Cqrs.Abstractions
    MediatR.Contracts

MicroKit.Cqrs.MediatR
    MicroKit.Cqrs.Abstractions
    MicroKit.Cqrs.MediatR.Abstractions
    MediatR

MicroKit.Cqrs.MediatR.Behaviors
    MicroKit.Cqrs.MediatR.Abstractions
    MediatR
    FluentValidation

MicroKit.Cqrs.MediatR.Caching
    MicroKit.Cqrs.Abstractions
    MicroKit.Cqrs.MediatR.Abstractions
    MicroKit.Caching.Abstractions
    MediatR
    Autofac
```
