using MicroKit.Domain.Events;

namespace MicroKit.MediatR.Events;

/// <summary>
/// Default implementation of <see cref="IDomainEventsDispatcher"/>.
/// Drains all domain events accumulated on tracked aggregates and dispatches them
/// to registered <see cref="IDomainEventHandler{TEvent}"/> implementations via
/// <see cref="IDomainEventHandlerDispatcher"/>.
/// </summary>
/// <remarks>
/// Registered as a <b>scoped</b> service. Called by <c>TransactionBehavior</c>
/// (in <c>MicroKit.MediatR.Behaviors</c>) after the command handler completes,
/// before <c>IUnitOfWork.CommitAsync</c>.
/// <para>
/// Command handlers must <b>not</b> call this interface directly. Domain events should
/// be accumulated on aggregates via <see cref="IDomainEventsProvider"/>; the dispatcher
/// is invoked by the pipeline behavior.
/// </para>
/// </remarks>
internal sealed class DomainEventDispatcher(
    IDomainEventsProvider eventsProvider,
    IDomainEventHandlerDispatcher handlerDispatcher) : IDomainEventsDispatcher
{
    /// <inheritdoc />
    public async Task DispatchEventsAsync(CancellationToken ct = default)
    {
        var events = eventsProvider.DrainDomainEvents();
        foreach (var domainEvent in events)
        {
            await handlerDispatcher.DispatchAsync(domainEvent, ct).ConfigureAwait(false);
        }
    }
}
