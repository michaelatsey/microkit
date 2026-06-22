namespace MicroKit.Messaging.EntityFrameworkCore;

// 'Result' is ambiguous here because the enclosing MicroKit namespace exposes MicroKit.Result as
// a sub-namespace. Alias placed in namespace scope with a distinct name to guarantee type resolution.
using R = global::MicroKit.Result.Result;

/// <summary>
/// EF Core implementation of <see cref="IOutboxWriter"/> and <see cref="IOutboxProcessorStore"/>.
/// Registered as scoped via <see cref="MessagingBuilderExtensions.AddEfCoreOutbox{TContext}"/>.
/// </summary>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> type.</typeparam>
internal sealed class EfOutboxStore<TContext>(TContext context) : IOutboxWriter, IOutboxProcessorStore
    where TContext : DbContext
{
    // IOutboxWriter — stages the row; caller's UoW (SaveChanges) commits it atomically
    // with domain changes. Do NOT call SaveChangesAsync here.
    /// <inheritdoc/>
    public ValueTask AddAsync(OutboxMessage message, CancellationToken ct = default)
    {
        context.Set<OutboxMessage>().Add(message);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask AddBatchAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default)
    {
        context.Set<OutboxMessage>().AddRange(messages);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await context.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(m =>
                (m.Status == OutboxMessageStatus.Pending ||
                 (m.Status == OutboxMessageStatus.Processing && m.LockedUntilUtc <= now))
                && (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= now))
            .OrderBy(m => m.OccurredOnUtc)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> AcquireLeaseAsync(
        MessageId id, DateTimeOffset lockExpiry, CancellationToken ct = default)
    {
        var callTime = DateTimeOffset.UtcNow;
        var rows = await context.Set<OutboxMessage>()
            .Where(m => m.Id == id
                && (m.Status == OutboxMessageStatus.Pending ||
                    (m.Status == OutboxMessageStatus.Processing && m.LockedUntilUtc <= callTime))
                && (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= callTime))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Processing)
                .SetProperty(m => m.LockedUntilUtc, lockExpiry),
                ct)
            .ConfigureAwait(false);
        return rows == 1;
    }

    /// <inheritdoc/>
    public async ValueTask<R> MarkPublishedAsync(
        MessageId id, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await context.Set<OutboxMessage>()
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Published)
                .SetProperty(m => m.ProcessedAtUtc, now)
                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null),
                ct)
            .ConfigureAwait(false);
        return R.Success();
    }

    /// <inheritdoc/>
    public async ValueTask<R> MarkFailedAsync(
        MessageId id, string errorMessage, int retryCount, CancellationToken ct = default)
    {
        var nextRetryAt = DateTimeOffset.UtcNow.Add(
            TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, retryCount))));
        await context.Set<OutboxMessage>()
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Pending)
                .SetProperty(m => m.RetryCount, retryCount)
                .SetProperty(m => m.ErrorMessage, errorMessage)
                .SetProperty(m => m.NextRetryAtUtc, nextRetryAt)
                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null),
                ct)
            .ConfigureAwait(false);
        return R.Success();
    }

    /// <inheritdoc/>
    public async ValueTask<R> DeadLetterAsync(
        MessageId id, string reason, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await context.Set<OutboxMessage>()
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Failed)
                .SetProperty(m => m.DeadLettered, true)
                .SetProperty(m => m.ProcessedAtUtc, now)
                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null)
                .SetProperty(m => m.ErrorMessage, reason),
                ct)
            .ConfigureAwait(false);
        return R.Success();
    }

    /// <inheritdoc/>
    public async ValueTask<int> DeleteProcessedAsync(
        DateTimeOffset olderThan, string tenantId, CancellationToken ct = default)
    {
        return await context.Set<OutboxMessage>()
            .Where(m => m.Status == OutboxMessageStatus.Published
                && m.ProcessedAtUtc < olderThan
                && m.TenantId == tenantId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<OutboxMessage>> GetDeadLetteredAsync(
        int batchSize, string tenantId, CancellationToken ct = default)
    {
        return await context.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(m => m.DeadLettered && m.TenantId == tenantId)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<R> RequeueAsync(
        MessageId id, CancellationToken ct = default)
    {
        await context.Set<OutboxMessage>()
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Pending)
                .SetProperty(m => m.DeadLettered, false)
                .SetProperty(m => m.RetryCount, 0)
                .SetProperty(m => m.NextRetryAtUtc, (DateTimeOffset?)null)
                .SetProperty(m => m.ErrorMessage, (string?)null)
                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null),
                ct)
            .ConfigureAwait(false);
        return R.Success();
    }
}
