namespace MicroKit.Messaging.Spi;

/// <summary>
/// The inbox guard of one store: what the engine needs to run a handler exactly once against that
/// store's database, with the handler's inbox row committed in the handler's own transaction.
/// </summary>
/// <remarks>
/// <para>
/// Part of the SPI (<c>MicroKit.Messaging.Spi</c>): a store provider implements it, registered
/// <b>scoped</b> and keyed by the store's <see cref="MessageStoreKey"/>. The engine calls it; an
/// application never does.
/// </para>
/// <para>
/// <b>One instance per scope, one scope per attempt.</b> The engine resolves an instance from a
/// scope opened for the handler to read the row state and to run the execution strategy, then a
/// fresh one from each attempt's own scope, on which it begins the transaction, writes the row,
/// runs the handler and commits. An instance therefore serves one unit of work: the one of the
/// scope it was resolved from, shared with the handler resolved from the same scope.
/// </para>
/// <para>
/// <b>An attempt, in order:</b> <see cref="BeginAsync"/>; then <see cref="EnlistProcessedAsync"/>
/// for a new delivery, or <see cref="MarkClaimedProcessedAsync"/> for a retry; then the handler;
/// then <see cref="CommitAsync"/>. Disposing the scope without committing rolls everything back.
/// </para>
/// </remarks>
public interface IInboxGuardStore
{
    /// <summary>
    /// Reads the current state of the row of <paramref name="key"/> from the database, without
    /// tracking it.
    /// </summary>
    /// <remarks>
    /// Never from a cache or a change tracker: it is the answer to "did an attempt whose commit
    /// outcome is unknown commit?", so only the database may give it.
    /// </remarks>
    /// <param name="key">The (message, handler) pair.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The row's state, or <see langword="null"/> when there is no row.</returns>
    ValueTask<InboxRowState?> FindAsync(InboxMessageKey key, CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="operation"/> as one operation of the execution strategy of this
    /// store's database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A strategy that retries runs <paramref name="operation"/> again after a transient fault;
    /// each run opens its own scope and transaction. After a fault whose commit outcome is unknown
    /// it calls <paramref name="verifySucceeded"/> first, and returns
    /// <paramref name="verifiedResult"/> without another run when that reports the commit.
    /// </para>
    /// <para>
    /// The last fault propagates <b>unwrapped</b>: a strategy's own "retry limit exceeded" wrapper
    /// is removed, so the engine classifies the fault itself.
    /// </para>
    /// </remarks>
    /// <typeparam name="TResult">The operation's result.</typeparam>
    /// <param name="operation">One attempt.</param>
    /// <param name="verifySucceeded">Whether an attempt whose commit outcome is unknown committed.</param>
    /// <param name="verifiedResult">The result when <paramref name="verifySucceeded"/> reports the commit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the attempt that completed.</returns>
    ValueTask<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        Func<CancellationToken, ValueTask<bool>> verifySucceeded,
        TResult verifiedResult,
        CancellationToken ct = default);

    /// <summary>
    /// Begins the attempt's transaction on this scope's unit of work. Every write of the attempt —
    /// the row, the handler's own saves, its publications — runs inside it.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the transaction is open.</returns>
    ValueTask BeginAsync(CancellationToken ct = default);

    /// <summary>
    /// Enlists the row of a new delivery, already <see cref="InboxMessageStatus.Processed"/>, in
    /// this scope's unit of work. Writes nothing yet.
    /// </summary>
    /// <remarks>
    /// The unique index on <c>(MessageId, ConsumerType)</c> is the guard: a concurrent delivery
    /// that commits the same row first makes this attempt's save fail, and the engine then finds
    /// the row and reports a duplicate.
    /// </remarks>
    /// <param name="row">The row to enlist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the row is enlisted.</returns>
    ValueTask EnlistProcessedAsync(InboxMessage row, CancellationToken ct = default);

    /// <summary>
    /// Marks the claimed row of a retry <see cref="InboxMessageStatus.Processed"/> in this scope's
    /// unit of work, conditioned on the claim. Writes nothing yet.
    /// </summary>
    /// <remarks>
    /// The save must fail when the row no longer carries <paramref name="claimToken"/>, so that a
    /// claim lost after this read is detected at commit (<see cref="IsLeaseLost"/>). It must hold
    /// under a unit of work whose queries do not track by default.
    /// </remarks>
    /// <param name="key">The (message, handler) pair.</param>
    /// <param name="claimToken">The token of the claim that owns the row.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="false"/> when the row does not carry the token any more: the claim is lost
    /// and the handler must not run.
    /// </returns>
    ValueTask<bool> MarkClaimedProcessedAsync(
        InboxMessageKey key, Guid claimToken, CancellationToken ct = default);

    /// <summary>Saves this scope's unit of work, then commits the attempt's transaction.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the transaction is committed.</returns>
    ValueTask CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether <paramref name="exception"/> is a retry's claim lost at commit: the conditioned
    /// write of <see cref="MarkClaimedProcessedAsync"/> matched no row.
    /// </summary>
    /// <remarks>
    /// Structural — which entry failed — never a guess from the message. Any other exception of a
    /// retry is the handler's failure, a business unique violation included.
    /// </remarks>
    /// <param name="exception">The exception the attempt raised.</param>
    /// <returns><see langword="true"/> when the claim was lost.</returns>
    bool IsLeaseLost(Exception exception);

    /// <summary>
    /// Inserts the row of a new delivery that failed, already <see cref="InboxMessageStatus.Failed"/>,
    /// and commits it in a transaction of its own. Never an upsert.
    /// </summary>
    /// <param name="row">The row to insert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="false"/> when a row of that (message, handler) already exists: another
    /// delivery owns it.
    /// </returns>
    ValueTask<bool> TryInsertFailedAsync(InboxMessage row, CancellationToken ct = default);
}
