using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Outbox;

internal class EfOutboxCleanupService<TContext>(TContext context) : IOutboxCleanupService
    where TContext : DbContext
{
    public async Task<int> CleanupAsync(
        DateTimeOffset olderThan,
        MessageStatus status,
        int batchSize,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {

        var query = context.Set<OutboxMessage>()
            .Where(m => m.Status == status);

        // Filtre crucial pour l'isolation et la performance des index
        if (!string.IsNullOrEmpty(tenantId))
        {
            query = query.Where(m => m.TenantId == tenantId);
        }

        query = status switch
        {
            MessageStatus.Published => query.Where(m => m.ProcessedAtUtc < olderThan),
            _ => query.Where(m => m.OccurredOnUtc < olderThan)
        };

        // Exécuter par batch pour ne pas locker la table trop longtemps
        // On limite à batchSize pour éviter de faire exploser le log de transaction
        // Note: EF Core 8 supporte mieux le Take() avant ExecuteDelete
        // Commande SQL atomique et ultra-performante
        query = query
            .OrderBy(m => m.OccurredOnUtc)
            .Take(batchSize);
        
#if DEBUG
        Console.WriteLine($"Executing cleanup query: {query.ToQueryString()}");
#endif

        return await query.ExecuteDeleteAsync(cancellationToken); 
    }
}
