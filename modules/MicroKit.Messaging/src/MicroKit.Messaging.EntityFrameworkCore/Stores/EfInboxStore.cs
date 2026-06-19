namespace MicroKit.Messaging.EntityFrameworkCore;

// 'Result' is ambiguous here because the enclosing MicroKit namespace exposes MicroKit.Result as
// a sub-namespace. Alias placed in namespace scope with a distinct name to guarantee type resolution.
using R = global::MicroKit.Result.Result;

/// <summary>
/// EF Core implementation of <see cref="IInboxStore"/>.
/// Registered as scoped via <see cref="MessagingBuilderExtensions.AddEfCoreOutbox{TContext}"/>.
/// </summary>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> type.</typeparam>
internal sealed class EfInboxStore<TContext>(TContext context) : IInboxStore
    where TContext : DbContext
{
    /// <inheritdoc/>
    public async ValueTask<bool> ExistsAsync(
        MessageId messageId, string consumerType, CancellationToken ct = default)
    {
        return await context.Set<InboxMessage>()
            .AsNoTracking()
            .AnyAsync(m => m.MessageId == messageId && m.ConsumerType == consumerType, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <c>SaveChangesAsync</c> immediately — there is no surrounding domain unit of work
    /// on the inbox write path (ADR-MSG-002). The compound PK <c>(MessageId, ConsumerType)</c>
    /// is the dedup gate: a <c>DbUpdateException</c> on duplicate must be caught by the caller
    /// (<c>InProcessIntegrationDispatcher</c>) and treated as a dedup skip.
    /// <para>
    /// <b>Warning:</b> <c>SaveChangesAsync</c> flushes ALL tracked changes on
    /// <typeparamref name="TContext"/>, not just the inbox row. If the same context instance is
    /// shared with domain entities, any outstanding domain changes will also be committed here.
    /// Use a dedicated messaging <see cref="DbContext"/> to avoid this hazard.
    /// </para>
    /// </remarks>
    public async ValueTask AddAsync(InboxMessage message, CancellationToken ct = default)
    {
        context.Set<InboxMessage>().Add(message);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<InboxMessage>> GetPendingAsync(
        int batchSize, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await context.Set<InboxMessage>()
            .AsNoTracking()
            .Where(m =>
                (m.Status == InboxMessageStatus.Received ||
                 (m.Status == InboxMessageStatus.Processing && m.LockedUntilUtc <= now))
                && (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= now))
            .OrderBy(m => m.ReceivedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask MarkProcessingAsync(
        MessageId messageId, string consumerType, DateTimeOffset lockUntil,
        CancellationToken ct = default)
    {
        await context.Set<InboxMessage>()
            .Where(m => m.MessageId == messageId && m.ConsumerType == consumerType)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, InboxMessageStatus.Processing)
                .SetProperty(m => m.LockedUntilUtc, lockUntil),
                ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<R> MarkProcessedAsync(
        MessageId messageId, string consumerType, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await context.Set<InboxMessage>()
            .Where(m => m.MessageId == messageId && m.ConsumerType == consumerType)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, InboxMessageStatus.Processed)
                .SetProperty(m => m.ProcessedAtUtc, now)
                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null),
                ct)
            .ConfigureAwait(false);
        return R.Success();
    }

    /// <inheritdoc/>
    public async ValueTask<R> MarkFailedAsync(
        MessageId messageId, string consumerType, string errorMessage,
        int retryCount, CancellationToken ct = default)
    {
        var nextRetryAt = DateTimeOffset.UtcNow.Add(
            TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, retryCount))));
        await context.Set<InboxMessage>()
            .Where(m => m.MessageId == messageId && m.ConsumerType == consumerType)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, InboxMessageStatus.Received)
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
        MessageId messageId, string consumerType, string reason,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await context.Set<InboxMessage>()
            .Where(m => m.MessageId == messageId && m.ConsumerType == consumerType)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, InboxMessageStatus.Failed)
                .SetProperty(m => m.DeadLettered, true)
                .SetProperty(m => m.ProcessedAtUtc, now)
                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null)
                .SetProperty(m => m.ErrorMessage, reason),
                ct)
            .ConfigureAwait(false);
        return R.Success();
    }
}
