namespace MicroKit.Messaging;

/// <summary>
/// Retention. Consumed by the cleanup worker, never by the processor.
/// </summary>
/// <remarks>
/// Split out of <see cref="IOutboxProcessorStore"/> to enforce ISP. Without a retention pass the
/// outbox grows without bound: every successfully dispatched message stays in the table forever.
/// </remarks>
public interface IOutboxRetentionStore
{
    /// <summary>Deletes published messages processed before <paramref name="olderThan"/>.</summary>
    /// <param name="olderThan">Exclusive upper bound on <see cref="OutboxMessage.ProcessedAtUtc"/>.</param>
    /// <param name="tenantId">
    /// Tenant filter. <see langword="null"/> means every tenant. The previous signature
    /// took a non-nullable string, so a single-tenant deployment — where every row has a
    /// null tenant — never matched and the outbox grew without bound.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Number of rows deleted.</returns>
    ValueTask<int> DeleteProcessedAsync(
        DateTimeOffset olderThan,
        string? tenantId = null,
        CancellationToken ct = default);
}
