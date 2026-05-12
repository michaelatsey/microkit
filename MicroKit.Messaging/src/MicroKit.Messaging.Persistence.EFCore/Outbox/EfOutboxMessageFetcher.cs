using MicroKit.Messaging.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.Persistence.EFCore.Outbox;

/// <summary>EF Core implementation of <see cref="IOutboxMessageFetcher"/> that locks the next batch of outbox messages using the configured strategy.</summary>
public class EfOutboxMessageFetcher<TContext> : IOutboxMessageFetcher
    where TContext : DbContext
{
    private readonly IOutboxLockingStrategy _lockingStrategy;
    private readonly IDbContextFactory<TContext> _factory;
    private readonly ILogger<EfOutboxMessageFetcher<TContext>> _logger;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="lockingStrategy">The strategy used to atomically lock outbox messages.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="factory">Factory for creating short-lived <typeparamref name="TContext"/> instances.</param>
    public EfOutboxMessageFetcher(IOutboxLockingStrategy lockingStrategy, ILogger<EfOutboxMessageFetcher<TContext>> logger, IDbContextFactory<TContext> factory)
    {
        _lockingStrategy = lockingStrategy;
        _logger = logger;
        _factory = factory;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboxMessage>> FetchNextBatchAsync(
        string tenantId,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var dbSet = context.Set<OutboxMessage>();
        // Appel de la stratégie de lock (SQL Server, Postgres ou Optimistic)
        var entities = await _lockingStrategy
            .LockNextAsync(context, tenantId, batchSize, lockDuration, cancellationToken);

        if (entities.Count == 0)
        {
            _logger.LogTrace("No pending outbox messages found");
            return [];
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Fetched and locked {Count} outbox messages",
                entities.Count);
        }

        //Mapping vers le domaine via notre Mapper statique
        return entities;
    }
}
