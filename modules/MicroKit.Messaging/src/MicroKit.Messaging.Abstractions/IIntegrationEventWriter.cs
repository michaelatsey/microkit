namespace MicroKit.Messaging;

/// <summary>
/// Staging port for integration events. Implemented by the persistence package, consumed only by
/// <see cref="IIntegrationEventPublisher"/>.
/// </summary>
/// <remarks>
/// Split from the publisher so that contract resolution, context capture and serialization stay
/// free of any storage dependency and unit-testable without a database.
/// </remarks>
public interface IIntegrationEventWriter
{
    /// <summary>Stages one row. Implementations must never call <c>SaveChangesAsync</c>.</summary>
    /// <param name="message">The row to stage.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <remarks>
    /// The token is unused today — staging performs no I/O — and the signature keeps it so the
    /// port stays compatible with an implementation that later validates or writes asynchronously.
    /// </remarks>
    ValueTask AddAsync(IntegrationEventMessage message, CancellationToken ct = default);

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
    /// A guard, not a proof: a caller could hold a transaction and never commit. It catches the
    /// failure that actually happens — publishing from somewhere with no unit of work — and costs
    /// nothing.
    /// </para>
    /// </remarks>
    bool HasOpenTransaction { get; }
}
