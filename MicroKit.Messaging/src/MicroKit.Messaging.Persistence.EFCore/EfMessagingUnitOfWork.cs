using MicroKit.Messaging.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore;

public class EfMessagingUnitOfWork<TContext>(TContext context): IMessagingUnitOfWork
    where TContext : DbContext
{
    public Task<int> SaveChangesAsync(CancellationToken cancellation = default)
    {
        return context.SaveChangesAsync(cancellation);
    }
    
}
