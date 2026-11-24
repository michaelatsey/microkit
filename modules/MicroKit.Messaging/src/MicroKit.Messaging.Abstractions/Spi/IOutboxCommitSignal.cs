namespace MicroKit.Messaging.Spi;

/// <summary>
/// Tells the engine that rows a store provider enlisted for a store were committed in this scope, so
/// the store's outbox loop claims them now rather than at its next poll (#215).
/// </summary>
/// <remarks>
/// <para>
/// Part of the <b>storage SPI</b> (<c>MicroKit.Messaging.Spi</c>): implemented by the engine,
/// registered with the first store, and called by a store provider, never by application code.
/// </para>
/// <para>
/// <b>Scoped.</b> Resolve it from the scope that enlists the rows, at the first enlistment: a scope that
/// is being disposed resolves nothing. Call it once the rows are visible to other connections — after
/// the commit, typically when the scope ends.
/// </para>
/// <para>
/// <b>It never throws and never blocks.</b> Nothing is woken for a store this host does not drain as
/// named: an unknown key, or a partitioned store outside a scope of one of its instances. A wake only
/// saves latency. The loop still polls, so a call that is missed or comes too early delays a row to
/// the next poll and never loses it. A wake starts a claim no sooner than <c>PollingInterval</c>
/// after the previous claim began, to the timer's precision, and it never cuts a back-off.
/// </para>
/// <para>
/// <b>Limits.</b> A transaction owned outside the scope — <c>UseTransaction</c> with a transaction
/// that commits later, or an ambient <c>TransactionScope</c> completed after the scope — is signalled
/// too early, and the row waits for the next poll. A row saved and then rolled back costs one empty
/// claim. A publication from a host scope to a partitioned store is not woken (#189), and nor is
/// another host (#214).
/// </para>
/// </remarks>
public interface IOutboxCommitSignal
{
    /// <summary>Rows of <paramref name="store"/> enlisted in this scope were committed.</summary>
    /// <param name="store">The store the rows were enlisted in.</param>
    void Committed(MessageStoreKey store);
}
