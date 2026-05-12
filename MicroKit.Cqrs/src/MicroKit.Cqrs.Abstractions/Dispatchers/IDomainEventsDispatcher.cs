namespace MicroKit.Cqrs.Abstractions.Dispatchers;

/// <summary>Dispatches accumulated domain events raised by aggregate roots during a unit of work.</summary>
public interface IDomainEventsDispatcher
{
    /// <summary>Publishes all pending domain events collected since the last dispatch.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DispatchEventsAsync(CancellationToken cancellationToken = default);
}
