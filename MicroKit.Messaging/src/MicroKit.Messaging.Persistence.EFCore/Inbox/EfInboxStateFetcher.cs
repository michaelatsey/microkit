using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.Persistence.EFCore.Inbox;

public class EfInboxStateFetcher<TContext> : IInboxStateFetcher
    where TContext : DbContext
{
    private readonly IInboxLockingStrategy _lockingStrategy;
    private readonly IDbContextFactory<TContext> _factory;
    private readonly ILogger<EfInboxStateFetcher<TContext>> _logger;

    public EfInboxStateFetcher(IInboxLockingStrategy lockingStrategy, ILogger<EfInboxStateFetcher<TContext>> logger, IDbContextFactory<TContext> factory)
    {
        _lockingStrategy = lockingStrategy;
        _logger = logger;
        _factory = factory;
    }
    public async Task<IReadOnlyList<InboxState>> FetchNextBatchAsync(
        string tenantId,
        string consumerName,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken =default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        // On demande à la stratégie de verrouiller les prochaines lignes 'Pending'
        // On passe le statut Pending pour filtrer
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var dbSet = context.Set<InboxState>();
        var entities = await _lockingStrategy.LockNextAsync(
            context,
            tenantId,
            consumerName,
            batchSize,
            lockDuration,
            cancellationToken);

        if (!entities.Any())
        {
            _logger.LogTrace("No pending inbox messages found");
            return [];
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Fetched and locked {Count} inbox messages",
                entities.Count);
        }

        // Mapping vers le domaine
        return entities;
    }
}
