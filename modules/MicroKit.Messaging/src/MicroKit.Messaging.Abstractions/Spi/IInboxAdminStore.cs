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
}
