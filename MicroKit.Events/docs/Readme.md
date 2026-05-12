# MicroKit.Events

Domain and integration event contracts for .NET 10. Full distributed tracing context — CorrelationId, CausationId, RequestId, IdempotencyKey, and a metadata dictionary — built into every event, with zero third-party dependencies in the contracts package.

---

## What makes this production-grade

**Richer tracing contract than most event libraries.** `IEvent` carries six tracing fields: `CorrelationId`, `CausationId`, `RequestId`, `IdempotencyKey`, a free-form `IReadOnlyDictionary<string, string>? Metadata`, and both `TimestampUtc` (when the event object was created) and `OccurredOnUtc` (when the business fact occurred — settable for event sourcing replay). A naive implementation has one or none of these.

**`MessageType` is computed, not declared.** `EventBase.MessageType` returns `GetType().FullName!`. There is no string property to keep in sync with the class name. Rename the class and the routing key updates automatically everywhere.

**`protected init` for tracing fields.** Subclasses pass tracing context through the constructor using C# init-only setters, keeping the properties immutable after construction. The compiler enforces this — there is no `set` to call later.

**Two distinct contracts by deployment boundary.** `IEvent` (domain events, in-process) carries the full six-field tracing surface. `IIntegrationEvent` (cross-service events) carries only `Id`, `OccurredOnUtc`, `MessageType`, and `CorrelationId` — the minimal contract needed across a service boundary without leaking internal request context.

---

## Installation

```shell
# Contracts — IEvent, IIntegrationEvent, IEventBase, IEventPublisher — zero deps
dotnet add package MicroKit.Events.Contracts

# EventBase, IntegrationEventBase concrete base classes
dotnet add package MicroKit.Events
```

---

## Usage

### Domain event (in-process)

Inherit from `EventBase` to get all tracing fields automatically.

```csharp
using MicroKit.Events;

public sealed class OrderShippedEvent : EventBase
{
    public Guid OrderId { get; }
    public string TrackingNumber { get; }

    public OrderShippedEvent(
        Guid orderId,
        string trackingNumber,
        string? correlationId = null,
        string? causationId = null)
    {
        OrderId = orderId;
        TrackingNumber = trackingNumber;
        CorrelationId = correlationId;   // protected init — set once, immutable
        CausationId = causationId;
    }
}
```

`EventBase` members supplied automatically:

| Member | Value |
|---|---|
| `Id` | `Guid.NewGuid()` — generated at construction |
| `TimestampUtc` | `DateTimeOffset.UtcNow` — when the object was created |
| `OccurredOnUtc` | `DateTimeOffset.UtcNow` — overridable via `protected init` for replay |
| `MessageType` | `GetType().FullName!` — computed, never declared |
| `CorrelationId` | `null` unless passed via `protected init` |
| `CausationId` | `null` unless passed via `protected init` |
| `RequestId` | `null` unless passed via `protected init` |
| `IdempotencyKey` | `null` unless passed via `protected init` |
| `Metadata` | `null` unless passed via `protected init` |

### Integration event (cross-service)

Use `IntegrationEventBase` for events published to the message broker. The contract is intentionally narrower — only what the receiving service needs.

```csharp
using MicroKit.Events;

public sealed class OrderConfirmedIntegrationEvent : IntegrationEventBase
{
    public Guid OrderId { get; }
    public string CustomerId { get; }
    public decimal TotalAmount { get; }

    public OrderConfirmedIntegrationEvent(
        Guid orderId,
        string customerId,
        decimal totalAmount,
        string? correlationId = null)
    {
        OrderId = orderId;
        CustomerId = customerId;
        TotalAmount = totalAmount;
        CorrelationId = correlationId;
    }
}
```

`IntegrationEventBase` supplies: `Id`, `OccurredOnUtc`, `MessageType` (`GetType().FullName!`), and `CorrelationId`. There is no `IdempotencyKey` or `RequestId` — those are internal concerns.

### Publishing via IEventPublisher

`IEventPublisher` is the in-process publishing contract. Wire an implementation (e.g. MediatR-backed) in your composition root.

```csharp
using MicroKit.Events.Contracts;

public sealed class DomainEventDispatcher
{
    private readonly IEventPublisher _publisher;

    public DomainEventDispatcher(IEventPublisher publisher) => _publisher = publisher;

    // Called after the unit of work commits — domain events carry aggregates' side effects
    public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct) =>
        _publisher.PublishRangeAsync(events.Cast<IEvent>(), ct);
}
```

For integration events, enqueue them into the Outbox rather than publishing directly:

```csharp
// Inside a command handler — written atomically with the aggregate save
var evt = new OrderConfirmedIntegrationEvent(order.Id, order.CustomerId, order.Total, correlationId);

await _outboxService.EnqueueAsync(
    tenantId: _tenant.Tenant!.Id,
    messageId: evt.Id.ToString(),
    payload: evt,
    destination: new OutboxDestination { PublishToBroker = true, BrokerTopic = "orders.confirmed" },
    correlationId: evt.CorrelationId,
    cancellationToken: ct);
```

See [MicroKit.Messaging](../MicroKit.Messaging/docs/Readme.md) for outbox setup.

### Event sourcing: override OccurredOnUtc

For replay scenarios where the business timestamp must differ from the object creation time:

```csharp
public sealed class OrderPlacedEvent : EventBase
{
    public Guid OrderId { get; }

    public OrderPlacedEvent(Guid orderId, DateTimeOffset occurredAt)
    {
        OrderId = orderId;
        OccurredOnUtc = occurredAt;   // protected init — set to historical timestamp
    }
}
```

---

## Configuration

No DI registration is required for either package. They are pure base classes and interfaces.

Register `IEventPublisher` implementations in the DI container of your composition root:

```csharp
// Example: MediatR-backed domain event publisher
services.AddScoped<IEventPublisher, MediatREventPublisher>();
```

---

## Package dependency graph

```
MicroKit.Events.Contracts
    (no NuGet dependencies)

MicroKit.Events
    MicroKit.Events.Contracts
```
