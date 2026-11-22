namespace MicroKit.Messaging.Spi;

/// <summary>
/// Dead-letter inspection and requeueing. Consumed by operator tooling, never by the processor.
/// </summary>
/// <remarks>
/// <para>
/// Part of the <b>storage SPI</b> (<c>MicroKit.Messaging.Spi</c>): implemented by a persistence
/// provider. Unlike the other store contracts, the engine never resolves it: its only consumer is
/// operator tooling a host builds — a dead-letter console or a requeue endpoint — and that tooling
/// imports this namespace to call it.
/// </para>
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

    /// <summary>
    /// Dead-letters one tenant's pending messages: the operator's way out for a tenant that will not
    /// recover, a de-provisioned one say (#124).
    /// </summary>
    /// <param name="tenantId">
    /// The tenant. Never <see langword="null"/>: a tenant's back-off holds that tenant's messages only,
    /// and a deployment whose messages carry no tenant has none to park.
    /// </param>
    /// <param name="reason">Recorded as each message's error text, so <see cref="GetDeadLetteredAsync"/> shows why.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of messages dead-lettered.</returns>
    /// <remarks>
    /// <para>
    /// Only the tenant's <see cref="OutboxMessageStatus.Pending"/> messages that no processor holds —
    /// not dead-lettered, no claim token — in one set-based statement. A message a processor holds at
    /// that instant is left to its batch, which settles it back to <c>Pending</c>: call again if it
    /// still waits. So is a row a crashed processor left claimed: it is held until its lease
    /// expires and the store reclaims and settles it; call again then. Each is marked <see cref="OutboxMessageStatus.Failed"/> and dead-lettered, its error
    /// text the reason, its processing time now; its retry count is left alone.
    /// </para>
    /// <para>
    /// The way back is <see cref="GetDeadLetteredAsync"/> narrowed to the tenant, then
    /// <see cref="RequeueAsync"/> for each message, once the tenant is healthy again. Nothing in the
    /// engine dead-letters a tenant on its own: a tenant in back-off holds no other tenant's messages,
    /// and a long outage must not lose its backlog.
    /// </para>
    /// </remarks>
    ValueTask<int> DeadLetterPendingAsync(string tenantId, string reason, CancellationToken ct = default);
}
