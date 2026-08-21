namespace MicroKit.Messaging;

/// <summary>
/// Dead-letter inspection and requeueing. Consumed by operator tooling, never by the processor.
/// </summary>
/// <remarks>
/// Split out of <see cref="IOutboxProcessorStore"/> to enforce ISP: one class may implement both,
/// but a DLQ console has no business seeing <see cref="IOutboxProcessorStore.ClaimBatchAsync"/>.
/// </remarks>
public interface IOutboxAdminStore
{
    /// <summary>Lists dead-lettered messages, optionally narrowed to one tenant.</summary>
    /// <param name="batchSize">Maximum number of messages to return.</param>
    /// <param name="tenantId">
    /// Tenant filter. <see langword="null"/> means every tenant, which is also the
    /// correct value in single-tenant deployments where
    /// <see cref="OutboxMessage.TenantId"/> is itself null. The previous signature took a
    /// non-nullable string, so those deployments matched nothing at all.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The dead-lettered messages, oldest first.</returns>
    ValueTask<IReadOnlyList<OutboxMessage>> GetDeadLetteredAsync(
        int batchSize,
        string? tenantId = null,
        CancellationToken ct = default);

    /// <summary>Returns a dead-lettered message to the queue with a cleared retry count.</summary>
    /// <param name="id">The message to requeue.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if a row was actually requeued. <see langword="false"/> means the
    /// message was not dead-lettered — the caller must be able to see that, rather than being
    /// told success unconditionally.
    /// </returns>
    ValueTask<bool> RequeueAsync(MessageId id, CancellationToken ct = default);
}
