using MicroKit.Persistence.Abstractions;

using static MicroKit.Result.Result;

namespace MicroKit.Messaging.MediatR.IntegrationTests.Fixtures;

// Distinct event types per scenario: ADR-MEDIATR-005 allows exactly one
// DomainEventNotification<TEvent> per event type, and every fixture in this assembly is registered
// in every container — so a scenario that reused another's event would change that scenario's
// handler count.

/// <summary>The domain fact, raised by the command and staged as a Notification row.</summary>
internal sealed record WidgetShippedEvent(Guid WidgetId, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

internal sealed class WidgetShippedNotification(WidgetShippedEvent domainEvent)
    : DomainEventNotification<WidgetShippedEvent>(domainEvent);

/// <summary>The published contract — what crosses the process boundary.</summary>
[MessageContract(WidgetShipped.Name)]
internal sealed record WidgetShipped(Guid WidgetId) : IIntegrationEvent
{
    internal const string Name = "microkit.e2e.widget-shipped";

    /// <summary>The wire type: the name and the default version 1.</summary>
    internal const string ContractName = Name + ".v1";
}

/// <summary>
/// The reentrant hop: a notification handler, in a local delivery, publishes an integration event,
/// which is enlisted as a transport row in the delivery's store and makes a second pass through the
/// same queue.
/// </summary>
/// <remarks>
/// Outbox by enlistment (#188): the bus adds the row to the dispatch scope's <c>DbContext</c>, and
/// <c>IUnitOfWork.CommitAsync</c> — a plain save, with no transaction of its own — commits it.
/// </remarks>
internal sealed class PublishWidgetShippedHandler(
    IMessageBus bus,
    IUnitOfWork unitOfWork,
    HandlerInvocationRecorder recorder)
    : INotificationHandler<WidgetShippedNotification>
{
    public async Task Handle(WidgetShippedNotification notification, CancellationToken cancellationToken)
    {
        recorder.Record(
            nameof(PublishWidgetShippedHandler),
            notification.DomainEvent.EventId.ToString());

        await bus.PublishAsync(new WidgetShipped(notification.DomainEvent.WidgetId), ct: cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}

internal sealed record ShipWidgetCommand(string Name) : ICommand<Result<Guid>>;

internal sealed class ShipWidgetHandler(IE2EWriter writer)
    : ICommandHandler<ShipWidgetCommand, Result<Guid>>
{
    public ValueTask<Result<Guid>> Handle(ShipWidgetCommand command, CancellationToken ct = default)
    {
        var aggregate = new E2EAggregate { Name = command.Name };
        aggregate.Raise(new WidgetShippedEvent(aggregate.Id, DateTimeOffset.UtcNow));
        writer.Stage(aggregate);
        return new(Success(aggregate.Id));
    }
}

/// <summary>
/// Records every envelope handed to the transport. No <c>IMessageTransport</c> implementation ships
/// in MicroKit, so the E2E suite supplies one — recording rather than mocking, so an assertion that
/// nothing was sent cannot pass by naming a method the subject does not call.
/// </summary>
internal sealed class RecordingMessageTransport : IMessageTransport
{
    public List<MessageEnvelope> Sent { get; } = [];

    public ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        Sent.Add(envelope);
        return ValueTask.CompletedTask;
    }
}
