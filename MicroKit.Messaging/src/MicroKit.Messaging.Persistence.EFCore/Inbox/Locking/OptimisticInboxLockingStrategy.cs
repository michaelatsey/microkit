using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Inbox.Locking;

internal sealed class OptimisticInboxLockingStrategy : IInboxLockingStrategy
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

        // Phase 1: read candidate IDs — order FIFO by InboxState.OccurredOnUtc (set at insert time, mirrors message order)
        var candidateIds = await dbSet
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.ConsumerName == consumerName &&
                (x.Status == MessageStatus.Pending || x.Status == MessageStatus.Failed) &&
                (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now) &&
                (x.LockedUntilUtc == null || x.LockedUntilUtc <= now))
            .OrderBy(x => x.OccurredOnUtc)
            .Take(batchSize)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0) return [];

        // Phase 2: atomic UPDATE with re-check to handle concurrent workers
        var affectedRows = await dbSet
            .Where(x => candidateIds.Contains(x.Id) &&
                        x.TenantId == tenantId &&
                        (x.Status == MessageStatus.Pending || x.Status == MessageStatus.Failed) &&
                        (x.LockedUntilUtc == null || x.LockedUntilUtc <= now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, MessageStatus.Processing)
                .SetProperty(x => x.LockedUntilUtc, lockUntil)
                .SetProperty(x => x.LastAttemptedAtUtc, now)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1),
                cancellationToken);

        if (affectedRows == 0) return [];

        // Phase 3: fetch the locked states (InboxMessage is loaded separately by the processor)
        return await dbSet
            .AsNoTracking()
            .Where(x => candidateIds.Contains(x.Id) &&
                        x.Status == MessageStatus.Processing &&
                        x.LockedUntilUtc == lockUntil)
            .ToListAsync(cancellationToken);
    }
}
