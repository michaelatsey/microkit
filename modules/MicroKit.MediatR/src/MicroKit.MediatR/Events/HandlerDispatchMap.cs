namespace MicroKit.MediatR.Events;

/// <summary>
/// Singleton compiled-delegate map used by <see cref="DomainEventHandlerDispatcher"/>.
/// Built at DI startup from the <see cref="IDomainEventHandler{TEvent}"/> scan —
/// no runtime reflection on the dispatch hot path.
/// </summary>
internal sealed class HandlerDispatchMap(
    Dictionary<Type, Func<IServiceProvider, IEvent, CancellationToken, Task>[]> map)
{
    internal bool TryGet(
        Type eventType,
        out Func<IServiceProvider, IEvent, CancellationToken, Task>[] delegates)
        => map.TryGetValue(eventType, out delegates!);
}
