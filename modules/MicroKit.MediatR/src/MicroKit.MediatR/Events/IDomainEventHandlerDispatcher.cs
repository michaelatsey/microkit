namespace MicroKit.MediatR.Events;

/// <summary>
/// Dispatches a raw domain event to all registered <see cref="IDomainEventHandler{TEvent}"/>
/// implementations for that event type.
/// </summary>
/// <remarks>
/// Registered as a <b>scoped</b> service. Resolved within the same DI scope as the
/// unit-of-work, ensuring that handlers called on the P3 synchronous dispatch path share
/// the same transaction context as the aggregate save.
/// <para>
/// Does not use MediatR's notification pipeline — handlers are invoked directly via a
/// pre-compiled delegate map built at DI startup, with zero per-dispatch reflection.
/// </para>
/// </remarks>
public interface IDomainEventHandlerDispatcher
{
    /// <summary>
    /// Dispatches <paramref name="domainEvent"/> to all handlers registered for its
    /// concrete type, in registration order.
    /// </summary>
    /// <param name="domainEvent">The domain event to dispatch.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    Task DispatchAsync(IEvent domainEvent, CancellationToken ct = default);
}
