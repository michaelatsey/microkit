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
/// <para>
/// <b>Only with a store.</b> A host with no store has no engine save after a handler, so the events a
/// handler leaves pending are not dispatched — their in-transaction handlers' writes would never be
/// saved — and are reported instead (event 2310). Registration cannot decide it:
/// <c>UseMediatRDomainEvents()</c> is order-independent with the store calls, so the host's store
/// set decides each delivery.
/// </para>
/// </remarks>
internal sealed class DomainEventsDispatchMiddleware(
    IMessageStoreSet stores,
    ILogger<DomainEventsDispatchMiddleware> logger) : IMessageReceiveMiddleware
{
    /// <inheritdoc />
    public async ValueTask InvokeAsync(MessageReceiveContext context, Func<ValueTask> proceed, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(proceed);
        await proceed().ConfigureAwait(false);

        // With a store, the engine saves the handler's work after this, so the events dispatched
        // here — their in-transaction handlers' writes and their cascade rows — commit with it.
        if (stores.GetStores().Count > 0)
        {
            await context.Services.GetRequiredService<IDomainEventsDispatcher>().DispatchEventsAsync(ct).ConfigureAwait(false);
            return;
        }

        // No store, no engine save: dispatching here would run in-transaction handlers whose writes
        // nothing saves (#188, review of PR #208). The events still pending are reported instead —
        // never dispatched, never silently dropped.
        if (context.Services.GetService<IDomainEventsProvider>()?.DomainEvents is { Count: > 0 } pending)
        {
            MediatRGlueLogs.EventsLeftUndispatched(
                logger, context.HandlerType.FullName ?? context.HandlerType.Name, pending.Count, Describe(pending));
        }
    }

    private static string Describe(IReadOnlyList<IDomainEvent> pending)
    {
        var names = new string[pending.Count];
        for (var i = 0; i < pending.Count; i++)
        {
            names[i] = pending[i].GetType().Name;
        }

        return string.Join(", ", names);
    }
}
