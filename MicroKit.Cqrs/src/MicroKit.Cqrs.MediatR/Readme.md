# MicroKit.Cqrs.MediatR

MediatR-backed implementations of `ICommandBus` and `IQueryBus`, plus abstract handler base classes that bridge MicroKit's handler interfaces with MediatR's `IRequestHandler<,>`. This is the only package that depends on MediatR within the CQRS stack.

## When to use

Use `MicroKit.Cqrs.MediatR` when your application uses MediatR as its in-process mediator and you want `ICommandBus`/`IQueryBus` as the dispatch abstraction instead of calling `ISender` directly.

For DI registration, also add `MicroKit.Cqrs.MediatR.Autofac` (Autofac) or wire the buses manually into Microsoft DI.

## Installation

```
dotnet add package MicroKit.Cqrs.MediatR
```

## Key types

| Type | Description |
|---|---|
| `MediatRCommandBus` | Routes `ICommand` to MediatR `ISender`; validates that the command also implements `IRequest<Unit>` or `IRequest<TResponse>` |
| `MediatRQueryBus` | Routes `IQuery<TResponse>` to MediatR `ISender`; validates that the query implements `IRequest<TResponse>` |
| `CommandHandler<TCommand>` | Abstract base for void command handlers; implements both `ICommandHandler<TCommand>` and `IRequestHandler<TCommand, Unit>` |
| `CommandHandler<TCommand, TResponse>` | Abstract base for command handlers with a return value |
| `QueryHandler<TQuery, TResponse>` | Abstract base for query handlers; implements both `IQueryHandler<TQuery, TResponse>` and `IRequestHandler<TQuery, TResponse>` |

## Usage

```csharp
// Command — must implement IRequest<Unit> for void dispatch
public record ConfirmOrderCommand(Guid OrderId)
    : ICommand, IRequest<Unit>;

// Handler — inherit CommandHandler<T> instead of implementing both interfaces manually
public class ConfirmOrderHandler : CommandHandler<ConfirmOrderCommand>
{
    private readonly IOrderRepository _repository;

    public ConfirmOrderHandler(IOrderRepository repository)
        => _repository = repository;

    public override async Task HandleAsync(ConfirmOrderCommand command, CancellationToken ct)
    {
        var order = await _repository.GetAsync(command.OrderId, ct);
        order.Confirm();
        await _repository.SaveAsync(order, ct);
    }
}

// Query — must implement IRequest<TResponse>
public record GetOrderQuery(Guid OrderId)
    : IQuery<OrderDto>, IRequest<OrderDto>;

public class GetOrderHandler : QueryHandler<GetOrderQuery, OrderDto>
{
    public override async Task<OrderDto> HandleAsync(GetOrderQuery query, CancellationToken ct)
    {
        // ...
    }
}
```

## Dependencies

- `MediatR`
- `MicroKit.Cqrs.Abstractions`
- `MicroKit.Abstractions`
