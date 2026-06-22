namespace MicroKit.MediatR.Events;

/// <summary>
/// Drains all domain events accumulated on aggregates and dispatches them through the
/// configured domain-event pipeline.
/// </summary>
/// <remarks>
/// Called by <c>TransactionBehavior</c> after the command handler completes, before
/// <c>IUnitOfWork.CommitAsync</c>. In the pure MediatR package this means direct
/// in-process dispatch to <see cref="IDomainEventHandler{TEvent}"/>. When the
/// Messaging.MediatR glue is installed, the implementation also writes mapped
/// notifications to the transactional outbox.
/// <para>
/// Command handlers must <b>not</b> call this interface directly. Domain events should
/// be accumulated on aggregates via <c>IDomainEventsProvider</c>; the dispatcher is
/// invoked by the pipeline behavior.
/// </para>
/// </remarks>
public interface IDomainEventsDispatcher
{
    /// <summary>
    /// Dispatches all currently accumulated domain events.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    Task DispatchEventsAsync(CancellationToken ct = default);
}

/// <summary>
/// Preview compatibility alias for <see cref="IDomainEventsDispatcher"/>.
/// </summary>
/// <remarks>
/// Renamed to <see cref="IDomainEventsDispatcher"/> to clarify that the dispatcher
/// drains all accumulated events in one call, not a single event.
/// Inject <see cref="IDomainEventsDispatcher"/> in new code.
/// This alias will be removed in the next major version.
/// </remarks>
[Obsolete("Use IDomainEventsDispatcher. This alias will be removed in the next major version.")]
public interface IDomainEventDispatcher : IDomainEventsDispatcher;
