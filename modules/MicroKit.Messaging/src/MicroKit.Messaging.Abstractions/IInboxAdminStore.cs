namespace MicroKit.Messaging;

/// <summary>Dead-letter inspection and requeueing. Consumed by operator tooling.</summary>
public interface IInboxAdminStore
{
    /// <summary>Lists dead-lettered rows, optionally narrowed to one tenant.</summary>
    /// <param name="batchSize">Maximum number of rows to return.</param>
    /// <param name="tenantId">
    /// Tenant filter. <see langword="null"/> means every tenant, which is also the correct value
    /// in single-tenant deployments where the tenant column is itself null.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dead-lettered rows, oldest first.</returns>
    ValueTask<IReadOnlyList<InboxMessage>> GetDeadLetteredAsync(
        int batchSize, string? tenantId = null, CancellationToken ct = default);

    /// <summary>Returns a dead-lettered row to the queue with a cleared retry count.</summary>
    /// <param name="key">The row to requeue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> only if a row was actually requeued. Never an unconditional
    /// success: zero rows means the message was not dead-lettered, which the operator must be
    /// able to see.
    /// </returns>
    ValueTask<bool> RequeueAsync(InboxMessageKey key, CancellationToken ct = default);
}
