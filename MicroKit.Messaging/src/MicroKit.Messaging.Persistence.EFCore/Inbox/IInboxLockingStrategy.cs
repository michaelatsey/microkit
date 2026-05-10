using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Inbox;

public interface IInboxLockingStrategy
{
    Task<IReadOnlyList<InboxState>> LockNextAsync(
        DbContext dbContext,
        string tenantId,
        string consumerName,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);
}
