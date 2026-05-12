using MicroKit.Messaging.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore;

/// <summary>EF Core implementation of <see cref="IMessagingUnitOfWork"/> that delegates to the provided <typeparamref name="TContext"/>.</summary>
/// <param name="context">The EF Core <see cref="DbContext"/> to save changes on.</param>
public class EfMessagingUnitOfWork<TContext>(TContext context): IMessagingUnitOfWork
    where TContext : DbContext
{
    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellation = default)
    {
        return context.SaveChangesAsync(cancellation);
    }
    
}
