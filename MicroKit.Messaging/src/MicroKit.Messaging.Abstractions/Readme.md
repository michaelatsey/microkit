# MicroKit.Messaging.Abstractions

Core contracts and data models for the Outbox/Inbox pattern. Defines envelope types, persistence interfaces, publisher interfaces, and the message type registry. Reference this package from any component that participates in reliable message delivery without committing to a specific persistence or transport implementation.

## When to use

Reference `MicroKit.Messaging.Abstractions` in:
- Publisher implementations (MediatR, Azure Service Bus, RabbitMQ)
- Persistence adapters (EF Core, Dapper)
- Background workers that process outbox messages
- Any code that constructs or inspects `EventEnvelope<T>` or `OutboxMessage`

Reference `MicroKit.Messaging.Core` for the concrete implementations of outbox/inbox repositories and the messaging builder.

## Installation

```
dotnet add package MicroKit.Messaging.Abstractions
```

## Key types

| Type | Description |
|---|---|
| `EventEnvelopeBase` | Non-generic envelope base with `EventId`, `TenantId`, `MessageType`, `OccurredOnUtc`, `PublishedAtUtc`, `CorrelationId`, `CausationId`, `IdempotencyKey`, `Metadata` |
| `EventEnvelope<T>` | Typed envelope wrapper; `Payload` is the strongly-typed event |
| `OutboxMessage` | Persisted outbox entry: `MessageType`, `Payload`, `PublishAsNotification`, status, retry counters |
| `InboxContext` | Received message context for inbox processing |
| `IOutboxRepository` | Persistence contract for storing and querying outbox messages |
| `IInboxRepository` | Persistence contract for deduplicating incoming messages |
| `IOutboxPublisher` | Dispatches a stored `OutboxMessage` to the appropriate consumer |
| `IInboxPublisher` | Processes an `InboxContext` and routes it to a handler |
| `IMessageTypeRegistry` | Maps message type names to CLR types for deserialization; supports explicit registration and AppDomain fallback |
| `IMicroKitMessageContextSetter` | Sets ambient correlation, causation, and idempotency context for outgoing messages |

## Usage

```csharp
// Build an envelope before writing to the outbox
var envelope = new EventEnvelope<OrderConfirmed>
{
    EventId = Guid.NewGuid().ToString(),
    TenantId = tenantId,
    MessageType = typeof(OrderConfirmed).FullName!,
    OccurredOnUtc = DateTimeOffset.UtcNow,
    PublishedAtUtc = DateTimeOffset.UtcNow,
    Payload = new OrderConfirmed(orderId)
};

// Write through the outbox repository
await outboxRepository.AddAsync(new OutboxMessage
{
    MessageType = envelope.MessageType,
    Payload = serializer.Serialize(envelope),
    PublishAsNotification = true
}, ct);
```

## Dependencies

- `MicroKit.Events.Contracts`
- `MicroKit.Abstractions`
