using static MicroKit.Result.Result;

namespace MicroKit.Messaging.MediatR.IntegrationTests.Fixtures;

/// <summary>
/// The staging port for command handlers. A <c>DbContext</c> constructor parameter in a handler is
/// flagged by <c>.claude/rules/no-handler-coupling.md</c>, and staging is all these handlers need —
/// the flush belongs to <c>TransactionBehavior</c>.
/// Precedent: <c>ITransactionTestWriter</c> in TransactionPersistenceFixtures.
/// </summary>
internal interface IE2EWriter
{
    void Stage(E2EAggregate aggregate);
}

internal sealed class EfE2EWriter(E2EDbContext context) : IE2EWriter
{
    // Stage only. Never SaveChangesAsync, never DiscardChanges — both exits belong to the behavior.
    public void Stage(E2EAggregate aggregate) => context.Add(aggregate);
}

/// <summary>The fact. One per scenario — ADR-MEDIATR-005 allows exactly one notification per event type.</summary>
internal sealed record WidgetCreatedEvent(Guid WidgetId, string Name) : DomainEvent;

/// <summary>
/// The outbox payload wrapper. The public constructor taking exactly <see cref="WidgetCreatedEvent"/>
/// is required twice over: <c>AddMicroKitMediatR</c>'s scan resolves it via
/// <c>GetConstructor([eventType])</c> to build the notification factory, and it is also the
/// constructor System.Text.Json uses to rehydrate the payload on the outbox dispatch path.
/// </summary>
internal sealed class WidgetCreatedNotification(WidgetCreatedEvent domainEvent)
    : DomainEventNotification<WidgetCreatedEvent>(domainEvent);

/// <summary>
/// Post-commit, at-least-once. Records its invocation; asserting on the recorder is how the test
/// observes that the notification actually fanned out.
/// </summary>
internal sealed class RecordWidgetCreatedHandler(HandlerInvocationRecorder recorder)
    : INotificationHandler<WidgetCreatedNotification>
{
    public Task Handle(WidgetCreatedNotification notification, CancellationToken cancellationToken)
    {
        recorder.Record(
            nameof(RecordWidgetCreatedHandler),
            notification.DomainEvent.EventId.ToString());
        return Task.CompletedTask;
    }
}

internal sealed record CreateWidgetCommand(string Name) : ICommand<Result<Guid>>;

internal sealed class CreateWidgetHandler(IE2EWriter writer)
    : ICommandHandler<CreateWidgetCommand, Result<Guid>>
{
    public ValueTask<Result<Guid>> Handle(CreateWidgetCommand command, CancellationToken ct = default)
    {
        var aggregate = new E2EAggregate { Name = command.Name };
        aggregate.Raise(new WidgetCreatedEvent(aggregate.Id, command.Name));

        // Stage only — TransactionBehavior drains the event and flushes both the aggregate and the
        // outbox row it produces, in one SaveChangesAsync inside the open transaction.
        writer.Stage(aggregate);

        return new(Success(aggregate.Id));
    }
}
