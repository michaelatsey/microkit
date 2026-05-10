using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Inbox;

internal sealed class EfInboxCleanupService<TContext>(
    IDbContextFactory<TContext> dbContextFactory)
    : IInboxCleanupService
    where TContext : DbContext
{
    public async Task<int> CleanupAsync(
        string tenantId,
        string consumerName,
        DateTimeOffset olderThan,
        MessageStatus status,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        await using var context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var states = context.Set<InboxState>()
            .Where(s => s.TenantId == tenantId && 
                        s.ConsumerName == consumerName && 
                        s.Status == status );

        // Logique de cutoff selon statut
        states = status switch
        {
            MessageStatus.Published =>
                states.Where(s => s.LastAttemptedAtUtc < olderThan),

            MessageStatus.Failed =>
                states.Where(s => s.LastAttemptedAtUtc < olderThan),

            _ =>
                states.Where(s => s.OccurredOnUtc < olderThan)
        };

        // Batch + OrderBy pour éviter lock massif
        states = states
            .OrderBy(s => s.OccurredOnUtc)
            .Take(batchSize);

#if DEBUG
        Console.WriteLine(states.ToQueryString());
#endif

        // 1️⃣ Suppression batchée des InboxStates
        var deletedStates =
            await states.ExecuteDeleteAsync(cancellationToken);

        if (deletedStates == 0)
            return 0;

        // 2. Suppression des messages orphelins (Globaux ou par Tenant selon ta structure)
        // Note: Si InboxMessage possède un TenantId, ajoute-le ici aussi.
        await context.Set<InboxMessage>()
            .Where(m => !context.Set<InboxState>()
                .Any(s => s.InboxMessageId == m.Id))
            .ExecuteDeleteAsync(cancellationToken);

        return deletedStates;
    }
}
