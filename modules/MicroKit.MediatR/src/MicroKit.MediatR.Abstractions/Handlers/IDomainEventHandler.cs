namespace MicroKit.MediatR.Handlers;

/// <summary>
/// Handles a domain event of type <typeparamref name="TEvent"/> synchronously,
/// within the same unit-of-work scope as the originating command handler.
/// Returns <see cref="Task"/> (not <see cref="ValueTask"/>) — consistent with
/// the MediatR <c>INotificationHandler</c> contract; domain event dispatch is
/// fire-and-forget within a transaction, not a value-bearing operation.
/// </summary>
/// <typeparam name="TEvent">The raw domain event type.</typeparam>
/// <remarks>
/// Invoked by <c>IDomainEventHandlerDispatcher</c> (in <c>MicroKit.MediatR</c>) — never
/// by MediatR's notification pipeline. Handlers registered here participate in the
/// in-transaction P3 synchronous dispatch path; they share the same
/// <see cref="IServiceProvider"/> scope as the aggregate save.
/// </remarks>
public interface IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>Handles the domain event.</summary>
    /// <param name="domainEvent">The raw domain event.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous handling.</returns>
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}
