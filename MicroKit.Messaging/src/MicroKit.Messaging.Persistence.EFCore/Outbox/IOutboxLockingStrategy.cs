using MicroKit.Messaging.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Outbox;

/// <summary>Strategy for atomically locking the next batch of pending outbox messages.</summary>
public interface IOutboxLockingStrategy
{
    /// <summary>Locks the next batch of pending outbox messages and returns them for processing.</summary>
    /// <param name="dbContext">The EF Core context to execute the lock operation against.</param>
    /// <param name="tenantId">The tenant whose messages to lock.</param>
    /// <param name="batchSize">Maximum number of messages to lock.</param>
    /// <param name="lockDuration">Duration for which the messages are locked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The locked outbox messages.</returns>
    Task<IReadOnlyList<OutboxMessage>> LockNextAsync(
        DbContext dbContext,
        string tenantId,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken);
}
