namespace MicroKit.Messaging.Abstractions.Persistence;

/// <summary>Unit-of-work abstraction for committing pending outbox/inbox changes to the database.</summary>
public interface IMessagingUnitOfWork
{
    /// <summary>Persists all pending changes tracked within the current messaging unit of work.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
