namespace MicroKit.Messaging.Abstractions.Persistence;

public interface IMessagingUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken  = default);
}
