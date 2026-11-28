namespace MicroKit.Messaging.Spi;

/// <summary>Dead-letter inspection and requeueing. Consumed by operator tooling.</summary>
/// <remarks>
/// <para>
/// Part of the <b>storage SPI</b> (<c>MicroKit.Messaging.Spi</c>): implemented by a
/// persistence provider. Unlike the other store contracts, the engine never resolves it: its only
/// consumer is operator tooling a host builds — a dead-letter console or a requeue endpoint — and
/// that tooling imports this namespace to call it.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Dead-letters one tenant's rows awaiting a retry: the operator's way out for a tenant whose
    /// handlers' dependency will not recover (#124).
    /// </summary>
    /// <param name="tenantId">
    /// The tenant, for every consumer of it. Never <see langword="null"/>: a back-off holds rows with a tenant
    /// only — a (consumer, tenant) pair's, or a consumer's for every tenant (#210) — and a deployment whose
    /// messages carry no tenant has none to park.
    /// </param>
    /// <param name="reason">Recorded as each row's error text, so <see cref="GetDeadLetteredAsync"/> shows why.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of rows dead-lettered.</returns>
    /// <remarks>
    /// <para>
    /// Only the tenant's rows that await an attempt and that no retry loop holds — status
    /// <see cref="InboxMessageStatus.Failed"/> or <see cref="InboxMessageStatus.Received"/>, not
    /// dead-lettered, no claim token — in one set-based statement. A row a loop holds at that instant is
    /// left to its batch: call again if it still waits. So is a row a crashed retry loop left claimed: it is held until its lease
    /// expires and the store reclaims and settles it; call again then. Each is left <see cref="InboxMessageStatus.Failed"/>
    /// and dead-lettered, its error text the reason, its processing time now; its retry count is left
    /// alone.
    /// </para>
    /// <para>
    /// The way back is <see cref="GetDeadLetteredAsync"/> narrowed to the tenant, then
    /// <see cref="RequeueAsync"/> for each row, once the tenant is healthy again. Nothing in the engine
    /// dead-letters a tenant on its own. It parks the rows of every consumer of the tenant, healthy ones
    /// included: it is no exit for one consumer whose own dependency will not recover (#210), whose rows
    /// dead-letter on sight once it is removed from the composition, as rows of an unknown consumer.
    /// </para>
    /// </remarks>
    ValueTask<int> DeadLetterPendingAsync(string tenantId, string reason, CancellationToken ct = default);
}
