namespace MicroKit.Messaging;

/// <summary>
/// Marks an inbox row processed <b>inside the handler's own unit of work</b>.
/// </summary>
/// <remarks>
/// <para>
/// This is what gives the inbox <b>transactionally atomic processing for database-backed
/// handlers</b>, and it is why inbox settlement is not batched the way outbox settlement is.
/// The guarantee is atomicity between the processed marker and database side effects performed
/// in the same transaction — not exactly-once in general. A handler that calls an external HTTP
/// endpoint and then rolls back will call that endpoint again on replay: database effects
/// happen effectively once, external effects at least once.
/// </para>
/// <para>
/// The outbox settles a whole batch at once. That is tolerable there <b>only</b> to the extent
/// that its consumers sit behind this inbox, where a redelivery costs duplication that the
/// unique index absorbs — it is not tolerable where the outbox dispatches to an in-process
/// handler with no inbox row, which is a known defect recorded on <c>OutboxProcessor</c>, not a
/// property to copy. For the inbox there is no downstream at all: the inbox <i>is</i> the
/// deduplication. A crash between a handler returning and its row being marked reruns the
/// handler, with its business side effects. Batching that settlement would turn one possible
/// replay into N, with nothing underneath to absorb them.
/// </para>
/// <para>
/// Resolved from the <b>per-message</b> execution scope, so the mark is staged on the same
/// <c>DbContext</c> the handler wrote through and commits in the same transaction. If the
/// transaction rolls back, the mark rolls back with it and the message is legitimately
/// reprocessed. If it commits, side effects and mark commit together — the window closes. The
/// extra round trip is free: it joins a transaction the handler was going to commit anyway.
/// </para>
/// <para>
/// <b>Ownership must be enforced at write time, not read time.</b> Filtering the read on the
/// claim token is not enough: the <c>UPDATE</c> that <c>SaveChanges</c> emits later carries only
/// the primary key, so a lease that expired in between would be silently overwritten — exactly
/// the race the token exists to prevent. Implementations must map
/// <see cref="InboxMessage.ClaimToken"/> as a concurrency token so the token lands in the
/// <c>WHERE</c> clause and a lost lease surfaces as a concurrency exception, rolling the handler
/// transaction back with it.
/// </para>
/// <para>
/// <b>Corollary worth naming.</b> A committed handler transaction leaves
/// <see cref="InboxMessage.ClaimToken"/> null, so every deferred write for that row — including
/// a release issued when the host is shutting down — filters to zero rows and becomes a no-op.
/// The token doubles as the "already settled" marker; nothing can undo a committed success.
/// </para>
/// <para>
/// Implementations must stage only. Never call <c>SaveChangesAsync</c>: the handler's unit of
/// work owns the transaction boundary.
/// </para>
/// <para>
/// <b>The read that stages the mark must participate in change tracking, and the implementation
/// must guarantee that rather than inherit it.</b> On an ORM whose read mode is configurable this
/// means stating it per query, not relying on the ambient default: an application that sets a
/// no-tracking default — an ordinary, widely recommended setting on a read-heavy
/// <c>DbContext</c>, and this store runs on the <i>consumer's</i> context — otherwise gets an
/// entity whose mutations belong to no unit of work. Nothing throws. The handler commits, writes
/// zero rows, and <see cref="IsMarkUncommitted"/> reports a mark that was never written as
/// committed; the row keeps its claim token, is re-claimed on every lease expiry, and replays
/// forever while each batch reports it processed. <b>An infinite replay reported as success</b> is
/// the failure mode, and it is silent — no warning, no metric, no growing retry count. This is not
/// hypothetical: it is the defect this contract shipped with, found in review and fixed in the EF
/// implementation. Stating it here is what stops the next implementer repeating it.
/// </para>
/// <para>
/// <b>Neither <see cref="IsMarkUncommitted"/> nor <see cref="IsLeaseLost"/> may throw.</b> The
/// processor consumes both inside exception filters, where a throwing predicate is swallowed and
/// evaluates to <see langword="false"/> — so an implementation that throws does not fail loudly,
/// it silently reports "committed" and "not a lost lease", which are the two answers that lose
/// work. Both are pure in-memory inspections by design: they take no
/// <see cref="CancellationToken"/> and must perform no I/O.
/// </para>
/// <para>
/// <b>Implementations without a change-tracking unit of work should not register this
/// contract.</b> A store that executes writes immediately cannot honour "stage only", and the
/// guarantee it exists to provide is unavailable to it. If such a store is registered anyway,
/// <see cref="IsMarkUncommitted"/> must return <see langword="true"/> — the conservative answer,
/// costing one redundant deferred write and a warning per row, rather than
/// <see langword="false"/>, which strands the row. Never leave the answer to chance: an
/// unanswerable question must resolve to the recoverable side.
/// </para>
/// </remarks>
public interface IInboxSettlementStore
{
    /// <summary>
    /// Stages the transition to processed for a row this processor owns. Does not commit.
    /// </summary>
    /// <param name="key">The row to settle.</param>
    /// <param name="claimToken">Ownership proof; the write must be a no-op without it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the mark was staged against a live claim,
    /// <see langword="false"/> when the lease had already been lost — in which case the caller
    /// must not invoke the handler.
    /// </returns>
    ValueTask<bool> StageProcessedAsync(
        InboxMessageKey key, Guid claimToken, CancellationToken ct = default);

    /// <summary>
    /// Determines whether a previously staged mark is still uncommitted after the handler
    /// returned.
    /// </summary>
    /// <param name="key">The row that was staged.</param>
    /// <returns>
    /// <see langword="true"/> when the staged change was never persisted — including whenever the
    /// implementation cannot tell. <see langword="false"/> only when the mark is known durable.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Without this check a handler that does no database work — a pure HTTP call, say — leaves
    /// the mark staged on a context nobody saves. The row is never marked processed, replays on
    /// every pass, and is eventually dead-lettered despite every handler invocation having
    /// succeeded. The processor uses this to fall back to a deferred
    /// <see cref="InboxOutcomeKind.Processed"/> outcome and to warn that the transactional
    /// guarantee did not apply, rather than degrading silently.
    /// </para>
    /// <para>
    /// <b>Absent means unknown, and unknown means uncommitted.</b> The question is "was the mark
    /// persisted?", not "is anything still queued" — a discarded staged change leaves nothing
    /// queued precisely because the write was thrown away. Reporting that as committed strands
    /// the row; reporting it as uncommitted costs one redundant write and a warning. The
    /// asymmetry decides the default.
    /// </para>
    /// <para>Must not throw — see the remarks on the interface.</para>
    /// </remarks>
    bool IsMarkUncommitted(InboxMessageKey key);

    /// <summary>
    /// Determines whether an exception raised by the handler's unit of work means this
    /// processor's lease was lost, rather than an ordinary domain fault.
    /// </summary>
    /// <param name="exception">The exception the handler's commit produced.</param>
    /// <returns>
    /// <see langword="true"/> when the exception is the concurrency token rejecting the write —
    /// the lease expired mid-handler and another processor owns the row. Must not throw; see the
    /// remarks on the interface.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This lives on the store rather than in the processor because the discrimination is a
    /// persistence-technology question: only the implementation knows which exception type its
    /// provider raises for a failed concurrency check, and which of its entries refer to an
    /// <see cref="InboxMessage"/>. Putting it here keeps <c>MicroKit.Messaging</c> free of any
    /// EF Core dependency, which its architecture tests enforce.
    /// </para>
    /// <para>
    /// The discrimination must be <b>structural</b> — which entity failed — never a guess at the
    /// exception's message. A lost lease and a domain conflict surface from the same
    /// <c>SaveChanges</c> as the same exception type; a domain conflict is legitimately
    /// transient and must keep its retry, while a lost lease must not, because the row is no
    /// longer this processor's to write.
    /// </para>
    /// <para>
    /// A lost lease rolls the handler's whole transaction back — business side effects and the
    /// staged mark together — so the row is left entirely untouched and counted in
    /// <see cref="InboxBatchResult.LeasesLost"/>.
    /// </para>
    /// </remarks>
    bool IsLeaseLost(Exception exception);
}
