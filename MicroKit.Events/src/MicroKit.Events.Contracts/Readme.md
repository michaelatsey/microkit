# MicroKit.Events.Contracts

Minimal event interface contracts with no external dependencies. These are the boundary types that event dispatchers, transport adapters, and consumers reference without depending on concrete event base classes.

## When to use

Reference `MicroKit.Events.Contracts` in:
- Packages that dispatch or route events (`IEventPublisher` implementations)
- Transport adapters that serialize/deserialize events
- Consumer handlers that receive events across service boundaries

Reference `MicroKit.Events` when you need the concrete base classes `EventBase` and `IntegrationEventBase` to define your own event types.

## Installation

```
dotnet add package MicroKit.Events.Contracts
```

## Key types

| Type | Description |
|---|---|
| `IEventBase` | Root contract: `Guid Id` and `DateTimeOffset OccurredOnUtc` |
| `IEvent` | Domain event contract; extends `IEventBase` with `MessageType`, `TimestampUtc`, `CorrelationId`, `IdempotencyKey`, `CausationId`, `RequestId`, `Metadata` |
| `IIntegrationEvent` | Cross-service event contract; extends `IEventBase` with `MessageType` and `CorrelationId` |
| `IEventPublisher` | `PublishAsync(IEvent)` and `PublishRangeAsync(IEnumerable<IEvent>)` |

## Usage

```csharp
// Dispatch via IEventPublisher
public class OrderService(IEventPublisher publisher)
{
    public async Task ConfirmAsync(Order order, CancellationToken ct)
    {
        order.Confirm();
        await publisher.PublishRangeAsync(order.GetEvents(), ct);
    }
}

// Consumer that depends only on the contract, not the concrete type
public class OrderConfirmedConsumer
{
    public Task ConsumeAsync(IEvent @event, CancellationToken ct)
    {
        // route by MessageType
        return Task.CompletedTask;
    }
}
```

## Dependencies

None.
