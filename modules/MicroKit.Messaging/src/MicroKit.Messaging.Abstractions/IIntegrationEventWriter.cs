namespace MicroKit.Messaging;

/// <summary>
/// Staging port for integration events. Implemented by the persistence package, consumed only by
/// <see cref="IIntegrationEventPublisher"/>.
/// </summary>
/// <remarks>
/// Split from the publisher so that contract resolution, context capture and serialization stay
/// free of any storage dependency and unit-testable without a database. It writes a
/// <see cref="MessageKind.Contract"/> row into the outbox — the same table the domain-event path
/// uses, routed by <see cref="OutboxMessage.MessageKind"/>.
/// </remarks>
public interface IIntegrationEventWriter
{
    /// <summary>Writes one contract row inside the caller's open transaction.</summary>
    /// <param name="message">The row to write.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// The row that now carries this publication — the one just written, or, when the replay key
    /// rejected it, the one already there. See <see cref="IntegrationEventWriteResult"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>It writes; it never commits.</b> That distinction is the whole contract, and the two are
    /// easy to conflate. An implementation flushes the row to the database because the replay key
    /// on (<see cref="OutboxMessage.OriginMessageId"/>, <see cref="OutboxMessage.ContractName"/>)
    /// can only be consulted by attempting the insert — but the write lands inside the transaction
    /// the caller opened, so a rollback still erases it and no event is announced for a fact that
    /// did not happen. <b>Never call <c>CommitAsync</c> on the caller's transaction.</b>
    /// </para>
    /// <para>
    /// <b>A redelivery is reported through the return value, never thrown.</b> A replayed dispatch
    /// re-runs its notification handlers, which publish the same contract from the same origin row.
    /// The publisher must be able to return normally, or every notification handler would carry a
    /// <c>try</c>/<c>catch</c> on a database exception. An exception from this method means a
    /// <b>real</b> failure and the caller must treat it as one.
    /// </para>
    /// <para>
    /// <b>The flush is not partial, and implementations must say so.</b> EF Core and its peers have
    /// no per-entity save, so flushing this row also flushes whatever else the caller had pending
    /// on the same unit of work. Inside one transaction that changes no outcome — same final state,
    /// same rollback — but it does change <i>when</i> the caller's own constraint violations
    /// surface. Verified for the EF Core implementation by
    /// <c>AbsorbedDuplicate_LeavesTheCallersOwnWritesIntact</c>.
    /// </para>
    /// </remarks>
    ValueTask<IntegrationEventWriteResult> AddAsync(
        OutboxMessage message, CancellationToken ct = default);

    /// <summary>
    /// Gets a value indicating whether an explicit transaction is currently open on the underlying
    /// unit of work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named for what it tests: an <i>open</i> transaction on this unit of work, not an ambient one
    /// in the <c>System.Transactions</c> sense.
    /// </para>
    /// <para>
    /// <b>It reflects the unit of work this writer holds.</b> If a caller opens its transaction on
    /// a different one, the guard passes while the row commits somewhere else — the same
    /// scope-identity assumption <see cref="IInboxSettlementStore"/> rests on, and the reason that
    /// identity is asserted by an integration test rather than trusted.
    /// </para>
    /// <para>
    /// <b>What it prevents is no longer a silent no-op.</b> When staging merely tracked an entity,
    /// publishing without a transaction lost the row to a discarded change tracker. Now that
    /// <see cref="AddAsync"/> flushes, the same mistake would <i>commit</i> the row through the
    /// provider's implicit per-statement transaction: an event announced permanently, for a fact
    /// the caller may still roll back. The guard runs before any other work for that reason.
    /// </para>
    /// </remarks>
    bool HasOpenTransaction { get; }
}
