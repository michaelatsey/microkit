using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.Persistence.EFCore.Inbox;

public class EFInboxMessageRepository<TContext> : IInboxMessageRepository
    where TContext : DbContext
{
    private readonly DbSet<InboxMessage> _dbSet;
    private readonly ILogger<EFInboxMessageRepository<TContext>> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public EFInboxMessageRepository(
        TContext context,
        ILogger<EFInboxMessageRepository<TContext>> logger)
    {
        ArgumentNullException.ThrowIfNull(context, nameof(context));
        _dbSet = context.Set<InboxMessage>();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async Task<bool> ExistsAsync(string tenantId, string messageId, CancellationToken cancellationToken = default)
    {
        if(string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));

        try
        {
            // Use raw SQL for performance on large datasets
            var exists = await _dbSet
                .AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId && x.Id == messageId,
                    cancellationToken);

            _logger.LogTrace(
                "Checked existence of message {MessageId} for tenant {TenantId}: {Exists}",
                messageId, tenantId, exists);

            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error checking if message {MessageId} exists for consumer {TenantId}",
                messageId, tenantId);
            throw;
        }
    }

    public async Task<InboxMessage?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _dbSet
                .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == id, ct);
        return entity;
    }

    public async Task<InboxMessage?> GetAsync(string tenantId, string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));


        try
        {
            var entity = await _dbSet
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.TenantId == tenantId &&
                    x.Id == messageId,
                    cancellationToken);

            return entity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error retrieving inbox message {MessageId} for tenant {tenantId}",
                messageId, tenantId);
            throw;
        }
    }

    public async Task AddAsync(InboxMessage message, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(message, cancellationToken);
    }    
}
