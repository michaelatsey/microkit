namespace MicroKit.Messaging.MediatR.Events;

/// <summary>
/// Dispatches the domain events a handler raised, after it returns and before the engine saves its
/// work (#188 D8): cascade events commit with the handler's work, and are delivered.
/// </summary>
/// <remarks>
/// <para>
/// A receive middleware, so it runs for every handler — a bus <see cref="IMessageHandler{TMessage}"/>
/// and a notification handler alike — in the handler's scope. Once the handler has returned,
/// <see cref="IDomainEventsDispatcher.DispatchEventsAsync"/> drains the events of the aggregates
/// the scope's unit of work tracks, runs their <see cref="IDomainEventHandler{TEvent}"/> handlers and
/// sends their notifications as local messages. In a guarded delivery all of it is inside the
/// attempt's transaction, so the engine's save commits it with the handler's row; a failed attempt
/// rolls it back, and the next attempt is a fresh scope with nothing collected.
/// </para>
/// <para>
/// A handler that throws dispatches nothing: the exception propagates before this step runs.
/// </para>
/// </remarks>
internal sealed class DomainEventsDispatchMiddleware(IDomainEventsDispatcher dispatcher) : IMessageReceiveMiddleware
{
    /// <inheritdoc />
    public async ValueTask InvokeAsync(MessageReceiveContext context, Func<ValueTask> proceed, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(proceed);
        await proceed().ConfigureAwait(false);
        await dispatcher.DispatchEventsAsync(ct).ConfigureAwait(false);
    }
}
