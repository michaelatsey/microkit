using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.Persistence.EFCore.Inbox;

/// <summary>EF Core implementation of <see cref="IInboxStateFetcher"/> that locks the next batch of pending inbox states.</summary>
public class EfInboxStateFetcher<TContext> : IInboxStateFetcher
    where TContext : DbContext
{
    private readonly IInboxLockingStrategy _lockingStrategy;
    private readonly IDbContextFactory<TContext> _factory;
    private readonly ILogger<EfInboxStateFetcher<TContext>> _logger;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="lockingStrategy">The strategy for atomically locking inbox states.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="factory">Factory for creating short-lived <typeparamref name="TContext"/> instances.</param>
    public EfInboxStateFetcher(IInboxLockingStrategy lockingStrategy, ILogger<EfInboxStateFetcher<TContext>> logger, IDbContextFactory<TContext> factory)
    {
        _lockingStrategy = lockingStrategy;
        _logger = logger;
        _factory = factory;
    }
    /// <inheritdoc/>
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
