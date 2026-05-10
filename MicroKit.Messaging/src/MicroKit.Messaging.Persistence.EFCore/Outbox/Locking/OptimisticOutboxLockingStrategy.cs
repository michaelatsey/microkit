using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Outbox.Locking;

internal sealed class OptimisticOutboxLockingStrategy : IOutboxLockingStrategy
{
    public async Task<IReadOnlyList<OutboxMessage>> LockNextAsync(
        DbContext dbContext,
        string tenantId,
        int batchSize,
        TimeSpan lockDuration, // On passe la durée du bail (ex: 5 min)
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var lockUntil = now.Add(lockDuration);
        var dbSet = dbContext.Set<OutboxMessage>();

        // 1. Sélection des candidats (Ids)
        // On cherche ce qui est Pending OU ce qui est Processing mais expiré
        var messageIds = await dbSet
            .Where(x => x.TenantId == tenantId &&
                        (x.Status == MessageStatus.Pending ||
                        (x.Status == MessageStatus.Processing && x.LockedUntilUtc <= now)) &&
                        (x.ScheduledAtUtc == null || x.ScheduledAtUtc <= now))
            .OrderBy(x => x.OccurredOnUtc)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (messageIds.Count == 0) return [];

        // 2. Verrouillage atomique (Optimistic concurrency via Status check)
        // On ne met à jour QUE si le statut n'a pas été changé par un autre worker entre-temps
        var affectedRows = await dbSet
            .Where(m => messageIds.Contains(m.Id) &&
                        (m.Status == MessageStatus.Pending || m.LockedUntilUtc <= now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MessageStatus.Processing)
                .SetProperty(m => m.LockedUntilUtc, lockUntil)
                .SetProperty(m => m.LastAttemptedAtUtc, now)
                .SetProperty(m => m.RetryCount, m => m.RetryCount + 1),
                cancellationToken);

        if (affectedRows == 0) return [];

        // 3. Récupération des données pour traitement
        // Note: On filtre à nouveau pour être sûr de ne prendre que ce que NOUS avons verrouillé
        return await dbSet
            .Where(m => messageIds.Contains(m.Id) &&
                        m.Status == MessageStatus.Processing &&
                        m.LockedUntilUtc == lockUntil)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
