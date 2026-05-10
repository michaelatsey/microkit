using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Inbox.Locking;

internal class OptimisticInboxLockingStrategy : IInboxLockingStrategy
{
    public async Task<IReadOnlyList<InboxState>> LockNextAsync(
        DbContext dbContext,
        string tenantId,
        string consumerName,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        var dbSet = dbContext.Set<InboxState>();
        var now = DateTimeOffset.UtcNow;
        var lockUntil = now.Add(lockDuration);

        // 1️⃣ Sélection des IDs candidats (avec TenantId et tri FIFO via Message)
        // On utilise AsNoTracking car c'est une requête de lecture seule.
        var candidateIds = await dbSet
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.ConsumerName == consumerName &&
                (x.Status == MessageStatus.Pending || x.Status == MessageStatus.Failed) &&
                (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now) &&
                (x.LockedUntilUtc == null || x.LockedUntilUtc <= now))
            .OrderBy(x => x.Message.OccurredOnUtc) // Tri FIFO basé sur la racine
            .Take(batchSize)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0) return [];

        // 2️⃣ UPDATE ATOMIQUE (Le verrouillage réel)
        // On re-vérifie les conditions dans le WHERE pour s'assurer qu'un autre worker 
        // n'a pas pris le message entre l'étape 1 et 2.
        var affectedRows = await dbSet
            .Where(x => candidateIds.Contains(x.Id) &&
                        x.TenantId == tenantId && // Sécurité supplémentaire
                        (x.Status == MessageStatus.Pending || x.Status == MessageStatus.Failed) &&
                        (x.LockedUntilUtc == null || x.LockedUntilUtc <= now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, MessageStatus.Processing)
                .SetProperty(x => x.LockedUntilUtc, lockUntil)
                .SetProperty(x => x.LastAttemptedAtUtc, now)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1),
                cancellationToken);

        // Si 0 ligne modifiée, c'est qu'un autre worker a été plus rapide sur tous les candidats.
        if (affectedRows == 0) return [];

        // 3️⃣ Récupération finale avec Payload et Type
        // On filtre par Status == Processing ET LockedUntilUtc pour être sûr de ne ramener 
        // que ce que CETTE instance vient de verrouiller.
        return await dbSet
            .AsNoTracking()
            .Include(x => x.Message) // Récupère la Payload et le MessageType
            .Where(x => candidateIds.Contains(x.Id) &&
                        x.Status == MessageStatus.Processing &&
                        x.LockedUntilUtc == lockUntil)
            .ToListAsync(cancellationToken);
    }
}
