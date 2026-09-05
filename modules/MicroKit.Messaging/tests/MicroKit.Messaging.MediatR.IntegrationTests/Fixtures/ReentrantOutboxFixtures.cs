using MicroKit.Persistence.Abstractions;

using static MicroKit.Result.Result;

namespace MicroKit.Messaging.MediatR.IntegrationTests.Fixtures;

// Distinct event types per scenario: ADR-MEDIATR-005 allows exactly one
// DomainEventNotification<TEvent> per event type, and every fixture in this assembly is registered
// in every container — so a scenario that reused another's event would change that scenario's
// handler count.

/// <summary>The domain fact, raised by the command and staged as a Notification row.</summary>
internal sealed record WidgetShippedEvent(Guid WidgetId) : DomainEvent;

internal sealed class WidgetShippedNotification(WidgetShippedEvent domainEvent)
    : DomainEventNotification<WidgetShippedEvent>(domainEvent);

/// <summary>The published contract — what crosses the process boundary.</summary>
[IntegrationEvent(WidgetShipped.ContractName)]
internal sealed record WidgetShipped(Guid WidgetId) : IIntegrationEvent
{
    internal const string ContractName = "microkit.e2e.widget-shipped.v1";
}

/// <summary>
/// The reentrant hop: a notification handler on the post-commit outbox path publishes an
/// integration event, which is staged as a <see cref="MessageKind.Contract"/> row and makes a
/// second pass through the same queue.
/// </summary>
/// <remarks>
/// It opens its own transaction through <c>ITransactionalContext.ExecuteAsync</c>, which is the
/// documented call site: <c>IUnitOfWork.CommitAsync</c> alone runs under the provider's implicit
/// per-call transaction and would be refused by the publisher's guard.
/// </remarks>
internal sealed class PublishWidgetShippedHandler(
    IIntegrationEventPublisher publisher,
    ITransactionalContext transaction,
    IUnitOfWork unitOfWork,
    HandlerInvocationRecorder recorder)
    : INotificationHandler<WidgetShippedNotification>
{
    public Task Handle(WidgetShippedNotification notification, CancellationToken cancellationToken)
    {
        recorder.Record(
            nameof(PublishWidgetShippedHandler),
            notification.DomainEvent.EventId.ToString());

        return transaction.ExecuteAsync(
            static async (state, token) =>
            {
                await state.Publisher.PublishAsync(
                    new WidgetShipped(state.WidgetId), state.OccurredAt, token);

                await state.UnitOfWork.CommitAsync(token);
            },
            (Publisher: publisher,
             UnitOfWork: unitOfWork,
             notification.DomainEvent.WidgetId,
             notification.DomainEvent.OccurredAt),
            cancellationToken);
    }
}

internal sealed record ShipWidgetCommand(string Name) : ICommand<Result<Guid>>;

internal sealed class ShipWidgetHandler(IE2EWriter writer)
    : ICommandHandler<ShipWidgetCommand, Result<Guid>>
{
    public ValueTask<Result<Guid>> Handle(ShipWidgetCommand command, CancellationToken ct = default)
    {
        var aggregate = new E2EAggregate { Name = command.Name };
        aggregate.Raise(new WidgetShippedEvent(aggregate.Id));
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
