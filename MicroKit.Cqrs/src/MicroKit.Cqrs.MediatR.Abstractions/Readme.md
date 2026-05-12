# MicroKit.Cqrs.MediatR.Abstractions

Supplemental contracts that bridge MicroKit's domain event and notification interfaces with MediatR's `INotification` system. Also provides the `IMediaTrPipelineRegistry` for ordered pipeline registration and the `MediatRNotificationWrapper<T>` for publishing domain events through MediatR.

## When to use

Reference this package when implementing packages that register MediatR-aware domain event or notification handlers, or that contribute to the MediatR pipeline registry. Most application code does not reference this directly — it is consumed by `MicroKit.Cqrs.MediatR.Autofac` and custom pipeline modules.

## Installation

```
dotnet add package MicroKit.Cqrs.MediatR.Abstractions
```

## Key types

| Type | Description |
|---|---|
| `IMediatRDomainEventHandler<TEvent>` | Combined `IDomainEventHandler<T>` + `INotificationHandler<T>` for domain events dispatched via MediatR |
| `IMediatRNotificationEventHandler<TNotification>` | Combined `INotificationEventHandler<T>` + `INotificationHandler<T>` for notification events |
| `IMediaTrPipelineRegistry` | Holds an ordered `List<Type>` of pipeline behavior types for pipeline construction |
| `MediatRNotificationWrapper<TEvent>` | Wraps a `DomainEvent` instance as `INotification` so it can be published through `IPublisher` |
| `PipelineRegistration` | Record of `(Type Type, int Order)` used to sort pipeline behaviors before registration |

## Usage

```csharp
// Domain event handler that is also a MediatR notification handler
public class OrderConfirmedHandler : IMediatRDomainEventHandler<OrderConfirmed>
{
    public Task HandleAsync(OrderConfirmed @event, CancellationToken ct)
    {
        // Satisfies INotificationHandler<OrderConfirmed> automatically
        return Task.CompletedTask;
    }

    // INotificationHandler<OrderConfirmed>.Handle is implemented by the base wiring
    public Task Handle(OrderConfirmed notification, CancellationToken cancellationToken)
        => HandleAsync(notification, cancellationToken);
}
```

## Dependencies

- `MediatR`
- `MicroKit.Cqrs.Abstractions`
- `MicroKit.Domain.Abstractions`
