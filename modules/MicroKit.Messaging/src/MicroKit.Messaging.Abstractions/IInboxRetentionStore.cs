namespace MicroKit.Messaging;

/// <summary>Retention. Consumed by the inbox cleanup worker only.</summary>
public interface IInboxRetentionStore
{
    /// <summary>Deletes processed rows older than the given instant.</summary>
    /// <param name="olderThan">Exclusive upper bound on the processed timestamp.</param>
    /// <param name="tenantId">
    /// Tenant filter. <see langword="null"/> means every tenant. The previous signature took a
    /// non-nullable string compared against a nullable column, so a single-tenant deployment —
    /// where every row has a null tenant — never matched and the table grew without bound.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of rows deleted.</returns>
    /// <remarks>
    /// Inbox retention is not housekeeping. The table only deduplicates messages it still holds,
    /// so the window must exceed the maximum plausible redelivery delay of every upstream
    /// transport. Deleting too eagerly reopens the door to reprocessing.
    /// </remarks>
    ValueTask<int> DeleteProcessedAsync(
        DateTimeOffset olderThan, string? tenantId = null, CancellationToken ct = default);
}
