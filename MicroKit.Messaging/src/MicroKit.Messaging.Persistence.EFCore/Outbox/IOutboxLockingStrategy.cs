using MicroKit.Messaging.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Outbox;

public interface IOutboxLockingStrategy
{
    Task<IReadOnlyList<OutboxMessage>> LockNextAsync(
        DbContext dbContext,
        string tenantId,
        int batchSize,
        TimeSpan lockDuration, // On passe la durée du bail (ex: 5 min)
        CancellationToken cancellationToken);
}
