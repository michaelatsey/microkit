namespace MicroKit.MediatR.Events;

/// <summary>
/// Drains all domain events accumulated on aggregates and dispatches them through the
/// 4-phase loop: GetAndClear → resolve notifications → publish → add to outbox.
/// </summary>
/// <remarks>
/// Called by <c>TransactionBehavior</c> after the command handler completes, before
/// <c>IUnitOfWork.CommitAsync</c> — ensuring domain events and their outbox entries
/// are persisted atomically with the aggregate changes.
/// <para>
/// Command handlers must <b>not</b> call this interface directly. Domain events should
/// be accumulated on aggregates via <c>IDomainEventsProvider</c>; the dispatcher is
/// invoked by the pipeline behavior.
/// </para>
/// </remarks>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches all accumulated domain events in a recursive drain loop.
    /// Continues until no new events are raised by domain event handlers.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    Task DispatchEventsAsync(CancellationToken ct = default);
}
