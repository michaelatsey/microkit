using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.Messaging.Persistence.EFCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace MicroKit.Messaging.Persistence.EFCore.Outbox;

public class EFOutboxRepository<TContext> : IOutboxRepository
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IOutboxLockingStrategy _lockingStrategy;
    private readonly DbSet<OutboxMessage> _dbSet;
    private readonly ILogger<EFOutboxRepository<TContext>> _logger;
    public EFOutboxRepository(TContext context, ILogger<EFOutboxRepository<TContext>> logger, IOutboxLockingStrategy lockingStrategy)
    {
        _context = context;
        _dbSet = context.Set<OutboxMessage>();
        _logger = logger;
        _lockingStrategy = lockingStrategy;
    }


    public async Task<OutboxMessage?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbSet.FindAsync([id], cancellationToken);
        //entity.Metadata = message.Metadata != null
        //    ? JsonSerializer.Serialize(message.Metadata)
        //    : null
        return entity;
    }
    // ==========================================
    // LE COEUR : VERROUILLAGE MULTI-TENANT
    // ==========================================
    public async Task<IReadOnlyList<OutboxMessage>> LockNextBatchAsync(
        string tenantId,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        return await _lockingStrategy.LockNextAsync(_context, tenantId, batchSize, lockDuration, cancellationToken);
    }

    public async Task<string> AddAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            await _dbSet.AddAsync(message, cancellationToken);
            return message.Id;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            _logger.LogInformation("Message ignoré : IdempotencyKey déjà traitée.");

            // On retourne l'ID actuel car le travail est techniquement "déjà fait" ou "en cours"
            // Cela permet à l'appelant de continuer sans planter.
            return message.Id;
        }
        
    }
    public async Task AddRangeAsync(IReadOnlyCollection<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddRangeAsync(messages, cancellationToken);
    }
    public async Task UpdateAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _dbSet.Update(message);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        // Suppression efficace sans chargement préalable pour le Cleanup
        await _dbSet.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<long> GetPendingCountAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .LongCountAsync(x => x.TenantId == tenantId && x.Status == MessageStatus.Pending, cancellationToken);
    }

    public Task ResetStuckProcessingMessagesAsync(
        DateTimeOffset olderThanUtc,
        CancellationToken cancellationToken = default)
    {
        return _dbSet
            .Where(x =>
                x.Status == MessageStatus.Processing &&
                x.LockedUntilUtc < olderThanUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, MessageStatus.Pending)
                .SetProperty(x => x.LockedUntilUtc, (DateTimeOffset?)null),
                cancellationToken);
    }

    public async Task<int> CleanupAsync(
        DateTimeOffset olderThan,
        MessageStatus status,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        // Utilisation de ExecuteDeleteAsync (.NET 7+) pour une performance maximale
        // sans charger les entités en mémoire.
        if (batchSize <= 0)
            throw new ArgumentException("Batch size must be > 0", nameof(batchSize));

        int totalDeleted = 0;
        int deleted;

        do
        {
            // On sélectionne les IDs d'abord dans une sous-requête
            var batchIds = _dbSet
                .Where(m => m.Status == status && m.OccurredOnUtc < olderThan)
                .OrderBy(m => m.OccurredOnUtc) // Optionnel ici mais aide à supprimer les plus vieux d'abord
                .Take(batchSize)
                .Select(m => m.Id);

            // On supprime les messages correspondants à ces IDs
            deleted = await _dbSet
                .Where(m => batchIds.Contains(m.Id))
                .ExecuteDeleteAsync(cancellationToken);

            totalDeleted += deleted;

            if (deleted == batchSize)
            {
                await Task.Delay(50, cancellationToken);
            }
        } while (deleted == batchSize);

        return totalDeleted;
    }

    /// <summary>
    /// Marks a message as failed after all retry attempts
    /// Moves it to a final failed state
    /// </summary>
    public async Task MarkAsFailedAsync(
        string messageId,
        string error,
        CancellationToken cancellationToken = default)
    {
        var affected = await _dbSet
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MessageStatus.Failed)
                .SetProperty(m => m.Error, error.Length > 2000 ? error[..2000] : error)
                .SetProperty(m => m.ProcessedAtUtc, DateTimeOffset.UtcNow),
                cancellationToken);

        if (affected > 0)
            _logger.LogWarning("Outbox message {Id} marked as Failed.", messageId);
    }

}
