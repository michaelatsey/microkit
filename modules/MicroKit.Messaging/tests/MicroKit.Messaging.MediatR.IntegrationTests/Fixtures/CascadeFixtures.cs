using static MicroKit.Result.Result;

namespace MicroKit.Messaging.MediatR.IntegrationTests.Fixtures;

// Distinct event types per scenario: ADR-MEDIATR-005 allows exactly one
// DomainEventNotification<TEvent> per event type, and AddMicroKitMediatR's assembly scan throws on
// a conflict. Every fixture in this assembly is registered in every container.

/// <summary>The first-level fact, raised by the command.</summary>
internal sealed record WidgetInspectedEvent(Guid WidgetId) : DomainEvent;

internal sealed class WidgetInspectedNotification(WidgetInspectedEvent domainEvent)
    : DomainEventNotification<WidgetInspectedEvent>(domainEvent);

/// <summary>
/// The second-level fact, raised by a notification handler on the outbox processing path — i.e.
/// AFTER the command's transaction has committed. This is the cascade.
/// </summary>
internal sealed record WidgetArchivedEvent(Guid WidgetId) : DomainEvent;

internal sealed class WidgetArchivedNotification(WidgetArchivedEvent domainEvent)
    : DomainEventNotification<WidgetArchivedEvent>(domainEvent);

/// <summary>
/// Runs on the post-commit outbox path and raises a NEW domain event on a newly tracked aggregate.
/// DomainEventsCascadeNotificationPublisher calls IDomainEventsDispatcher.DispatchEventsAsync once
/// after all handlers for a notification complete, so this new event is drained and its notification
/// staged to the outbox — within the processor's per-message scope.
/// </summary>
internal sealed class ArchiveWidgetOnInspectionHandler(
    IE2EWriter writer,
    HandlerInvocationRecorder recorder)
    : INotificationHandler<WidgetInspectedNotification>
{
    public Task Handle(WidgetInspectedNotification notification, CancellationToken cancellationToken)
    {
        recorder.Record(
            nameof(ArchiveWidgetOnInspectionHandler),
            notification.DomainEvent.EventId.ToString());

        // Staged on the PROCESSOR scope's DbContext — a different instance from the one the command
        // used, because PassThroughExecutionScopeFactory builds the per-message scope from the root
        // IServiceScopeFactory. EfDomainEventsProvider drains
        // ChangeTracker.Entries<IHasDomainEvents>() on THIS context.
        var archived = new E2EAggregate { Name = "archived" };
        archived.Raise(new WidgetArchivedEvent(archived.Id));
        writer.Stage(archived);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Would run if the cascade event ever reached the outbox and was dispatched. Whether it records
/// anything is the whole question.
/// </summary>
internal sealed class RecordWidgetArchivedHandler(HandlerInvocationRecorder recorder)
    : INotificationHandler<WidgetArchivedNotification>
{
    public Task Handle(WidgetArchivedNotification notification, CancellationToken cancellationToken)
    {
        recorder.Record(
            nameof(RecordWidgetArchivedHandler),
            notification.DomainEvent.EventId.ToString());
        return Task.CompletedTask;
    }
}

internal sealed record InspectWidgetCommand(string Name) : ICommand<Result<Guid>>;

internal sealed class InspectWidgetHandler(IE2EWriter writer)
    : ICommandHandler<InspectWidgetCommand, Result<Guid>>
{
    public ValueTask<Result<Guid>> Handle(InspectWidgetCommand command, CancellationToken ct = default)
    {
        var aggregate = new E2EAggregate { Name = command.Name };
        aggregate.Raise(new WidgetInspectedEvent(aggregate.Id));
        writer.Stage(aggregate);
        return new(Success(aggregate.Id));
    }
}
