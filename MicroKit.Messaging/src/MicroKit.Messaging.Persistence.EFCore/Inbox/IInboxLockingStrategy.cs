using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Inbox;

/// <summary>Strategy for atomically locking the next batch of pending inbox states.</summary>
public interface IInboxLockingStrategy
{
    /// <summary>Locks the next batch of pending inbox states and returns them for processing.</summary>
    /// <param name="dbContext">The EF Core context to execute the lock operation against.</param>
    /// <param name="tenantId">The tenant whose states to lock.</param>
    /// <param name="consumerName">The consumer that will process the states.</param>
    /// <param name="batchSize">Maximum number of states to lock.</param>
    /// <param name="lockDuration">Duration for which the states are locked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The locked inbox states.</returns>
    Task<IReadOnlyList<InboxState>> LockNextAsync(
        DbContext dbContext,
        string tenantId,
        string consumerName,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);
}
