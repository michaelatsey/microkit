# MicroKit.Messaging.Publisher.MediatR

MediatR-backed implementations of `IOutboxPublisher` and `IInboxPublisher`. The outbox publisher deserializes stored `OutboxMessage` payloads into typed `EventEnvelope<T>` instances and dispatches them as MediatR `INotification` objects. The inbox publisher routes `InboxContext` to MediatR commands or notifications.

## When to use

Use this package when your application already uses MediatR for in-process messaging and you want outbox messages dispatched through the same MediatR pipeline. Replace with a custom `IOutboxPublisher` if you need direct transport integration (Azure Service Bus, RabbitMQ) instead of in-process dispatch.

## Installation

```
dotnet add package MicroKit.Messaging.Publisher.MediatR
```

## Key types

| Type | Description |
|---|---|
| `MediatROutboxPublisher` | Resolves the payload CLR type via `IMessageTypeRegistry` (or AppDomain scan as fallback), deserializes the `EventEnvelope<T>`, sets ambient context (tenant, correlation, causation, idempotency), then publishes the payload as `INotification` |
| `MediatRInboxPublisher` | Routes an `InboxContext` to MediatR `IPublisher` as either a command or notification |
| `MediatRRegistrationExtensions.UseMediatRPublisher()` | Registers both publishers as scoped via `MicroKitMessagingBuilder` |

## Usage

```csharp
// Registration
services
    .AddMicroKitMessaging()
    .UseMediatRPublisher();

// Register message types so the publisher can resolve CLR types from stored type names
services.AddSingleton<IMessageTypeRegistry>(sp =>
{
    var registry = new MessageTypeRegistry();
    registry.Register<OrderConfirmed>();
    return registry;
});
```

The outbox dispatcher background service calls `IOutboxPublisher.PublishAsync(storedMessage)` for each pending record. `MediatROutboxPublisher` unwraps the envelope and calls `IPublisher.Publish(notification)`, which fans out to all registered MediatR `INotificationHandler<T>` implementations.

## Dependencies

- `MediatR`
- `MicroKit.Messaging.Core`
