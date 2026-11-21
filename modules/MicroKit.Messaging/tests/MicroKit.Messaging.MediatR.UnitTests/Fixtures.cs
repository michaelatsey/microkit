namespace MicroKit.Messaging.MediatR.UnitTests;

public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderShipped(Guid EventId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class OrderPlacedNotification(OrderPlaced domainEvent) : DomainEventNotification<OrderPlaced>(domainEvent);

/// <summary>An abstract domain-event notification: no handler of it can ever be matched (C3).</summary>
public abstract class AbstractOrderNotification(OrderPlaced domainEvent) : DomainEventNotification<OrderPlaced>(domainEvent);

/// <summary>A notification that is not a domain event's: not a local message.</summary>
public sealed record PlainNotification : INotification;

/// <summary>Counts its calls, by handler.</summary>
public sealed class CallLog
{
    public List<string> Calls { get; } = [];
}

public sealed class FirstOrderHandler(CallLog log) : INotificationHandler<OrderPlacedNotification>
{
    public Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
    {
        log.Calls.Add(nameof(FirstOrderHandler));
        return Task.CompletedTask;
    }
}

public sealed class SecondOrderHandler(CallLog log) : INotificationHandler<OrderPlacedNotification>
{
    public Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
    {
        log.Calls.Add(nameof(SecondOrderHandler));
        return Task.CompletedTask;
    }
}

public sealed class AbstractOrderHandler : INotificationHandler<AbstractOrderNotification>
{
    public Task Handle(AbstractOrderNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class PlainHandler : INotificationHandler<PlainNotification>
{
    public Task Handle(PlainNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class OpenHandler<TNotification> : INotificationHandler<TNotification>
    where TNotification : INotification
{
    public Task Handle(TNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Records what the sink sent, in order.</summary>
public sealed class RecordingLocalSender : ILocalMessageSender
{
    public List<(object Message, MessageId Id, DateTimeOffset OccurredOnUtc)> Sent { get; } = [];

    public ValueTask SendAsync(object message, MessageId messageId, DateTimeOffset occurredOnUtc, CancellationToken ct = default)
    {
        Sent.Add((message, messageId, occurredOnUtc));
        return ValueTask.CompletedTask;
    }
}

/// <summary>A handler whose dependency is never registered: it cannot be built.</summary>
public sealed class UnbuildableOrderHandler(IUnregisteredDependency dependency) : INotificationHandler<OrderPlacedNotification>
{
    public Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
        => Task.FromResult(dependency);
}

public interface IUnregisteredDependency;

public sealed class DisposableOrderHandler(CallLog log) : INotificationHandler<OrderPlacedNotification>, IDisposable
{
    public Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
    {
        log.Calls.Add(nameof(DisposableOrderHandler));
        return Task.CompletedTask;
    }

    public void Dispose() => log.Calls.Add("disposed");
}

public readonly record struct StructPlaced(Guid EventId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class StructPlacedNotification(StructPlaced domainEvent) : DomainEventNotification<StructPlaced>(domainEvent);

public sealed class StructPlacedHandler : INotificationHandler<StructPlacedNotification>
{
    public Task Handle(StructPlacedNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}
