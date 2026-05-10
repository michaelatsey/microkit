namespace MicroKit.Cqrs.Abstractions.Dispatchers;

public interface IDomainEventsDispatcher
{
    Task DispatchEventsAsync(CancellationToken cancellationToken = default);
}
