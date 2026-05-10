using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.Persistence.EFCore.Inbox;

internal class EfInboxStateRepository<TContext> : IInboxStateRepository
    where TContext : DbContext
{
    private readonly ILogger<EfInboxStateRepository<TContext>> _logger;
    private readonly DbSet<InboxState> _set;
    private readonly TContext _dbContext;
    private readonly IInboxLockingStrategy _lockingStrategy;

    public EfInboxStateRepository(
        TContext context,
        ILogger<EfInboxStateRepository<TContext>> logger,
        IInboxLockingStrategy lockingStrategy)
    {
        ArgumentNullException.ThrowIfNull(context, nameof(context));
        _dbContext = context;
        _set = context.Set<InboxState>();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lockingStrategy = lockingStrategy;
    }


    // LOCK
    public Task<IReadOnlyList<InboxState>> LockNextBatchAsync(
        string tenantId,
        string consumerName,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
        => _lockingStrategy.LockNextAsync(
            _dbContext,
            tenantId,
            consumerName,
            batchSize,
            lockDuration,
            cancellationToken);

    

    // COMPLETE (batch update atomique)
    public Task MarkConsumedAsync(
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return Task.CompletedTask;

        var now = DateTimeOffset.UtcNow;

        return _set
            .Where(x => ids.ToList().Contains(x.Id.ToString()))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, MessageStatus.Consumed)
                .SetProperty(x => x.ProcessedAtUtc, now)
                .SetProperty(x => x.LockedUntilUtc, (DateTimeOffset?)null),
                cancellationToken);
    }

    // FAIL (single optimized update)
    public Task MarkFailedAsync(
        string id,
        string error,
        DateTimeOffset? nextRetryAt,
        CancellationToken cancellationToken = default)
    {
        return _set
            .Where(x => x.Id.ToString() == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, nextRetryAt.HasValue ? MessageStatus.Failed : MessageStatus.DeadLettered)
                .SetProperty(x => x.LastError, error)
                .SetProperty(x => x.NextAttemptAtUtc, nextRetryAt)
                .SetProperty(x => x.LockedUntilUtc, (DateTimeOffset?)null),
                cancellationToken);
    }

    // BACKLOG METRIC (no tracking)
    public Task<int> GetBacklogCountAsync(
        string consumerName,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        return _set
            .AsNoTracking()
            .Where(x =>
                x.ConsumerName == consumerName &&
                (x.Status == MessageStatus.Pending ||
                 x.Status == MessageStatus.Failed) &&
                (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now))
            .CountAsync(cancellationToken);
    }

    
}
