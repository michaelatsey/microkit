namespace MicroKit.MediatR.Events;

internal sealed class DomainEventHandlerDispatcher(
    HandlerDispatchMap map,
    IServiceProvider sp) : IDomainEventHandlerDispatcher
{
    /// <inheritdoc />
    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct = default)
    {
        var eventType = domainEvent.GetType();
        if (!map.TryGet(eventType, out var delegates)) return;

        foreach (var invoke in delegates)
            await invoke(sp, domainEvent, ct).ConfigureAwait(false);
    }
}
