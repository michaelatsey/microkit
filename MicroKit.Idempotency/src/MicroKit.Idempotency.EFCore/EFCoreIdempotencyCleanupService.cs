using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Abstractions.Models;
using MicroKit.Idempotency.EFCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Idempotency.EFCore;

internal class EFCoreIdempotencyCleanupService<TContext>(TContext context) : IIdempotencyCleanupService
    where TContext : DbContext
{
    public async Task<int> CleanupAsync(
        DateTimeOffset olderThan,
        IdempotencyStatus status,
        int batchSize,
        CancellationToken cancellationToken = default)
    {

        var query = context.Set<IdempotencyRecord>()
            .Where(m => m.Status == status);

        query = status switch
        {
            IdempotencyStatus.Completed => query.Where(m => 
                m.CompletedAtUtc.HasValue && 
                m.CompletedAtUtc.Value < olderThan),
            _ => query.Where(m => 
                m.ExpiresAtUtc.HasValue &&  
                m.ExpiresAtUtc < olderThan)
        };

        // Exécuter par batch pour ne pas locker la table trop longtemps
        // On limite à batchSize pour éviter de faire exploser le log de transaction
        // Note: EF Core 8 supporte mieux le Take() avant ExecuteDelete
        // Commande SQL atomique et ultra-performante
        query = query
            .OrderBy(m => m.CreatedAtUtc)
            .Take(batchSize);
        
#if DEBUG
        Console.WriteLine($"Executing cleanup query: {query.ToQueryString()}");
#endif

        return await query.ExecuteDeleteAsync(cancellationToken); 
    }
}
