namespace MicroKit.Messaging;

/// <summary>
/// Inbox ingestion. Consumed by publishers and broker adapters, never by the processor.
/// </summary>
/// <remarks>
/// Split out under ISP: the processor has no business seeing <see cref="AddAsync"/>, and a
/// publisher has no business seeing <see cref="IInboxProcessorStore.ClaimBatchAsync"/>.
/// </remarks>
public interface IInboxWriter
{
    /// <summary>
    /// Determines whether a row already exists for this message and consumer.
    /// <b>Diagnostic only — never use this to decide whether to call
    /// <see cref="AddAsync"/>.</b>
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="consumerType">The assembly-qualified handler type name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> when the row is already recorded.</returns>
    /// <remarks>
    /// <para>
    /// The obvious use of this method is the wrong one. Guarding an insert with it —
    /// <c>if (!await ExistsAsync(...)) await AddAsync(...)</c> — is a
    /// time-of-check-to-time-of-use race: two ingesters both observe <see langword="false"/>, both
    /// insert, and one gets a constraint violation that the guard was supposed to prevent. The
    /// unique index on <c>(MessageId, ConsumerType)</c> is the sole authority on deduplication,
    /// and <see cref="AddAsync"/> already absorbs the violation and reports
    /// <see cref="InboxWriteResult.AlreadyPresent"/>. Call <see cref="AddAsync"/> unconditionally
    /// and read its result.
    /// </para>
    /// <para>
    /// It exists for operator tooling and for an implementation's own post-insert verification —
    /// asking "is the row recorded now?" <i>after</i> a failed write, which is a question with no
    /// race in it. The inbox drain path never calls it at all.
    /// </para>
    /// </remarks>
    ValueTask<bool> ExistsAsync(
        MessageId messageId, string consumerType, CancellationToken ct = default);

    /// <summary>
    /// Records an inbox row for one message and consumer.
    /// </summary>
    /// <param name="message">The row to record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="InboxWriteResult.Added"/> when the row was inserted, or
    /// <see cref="InboxWriteResult.AlreadyPresent"/> when the unique index on
    /// <c>(MessageId, ConsumerType)</c> already held it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Redelivery is reported through the return value, never through an exception.</b> The
    /// unique index is the dedup gate, and the gate holding is the gate working: under
    /// at-least-once delivery a redelivery needs no failure at all — an expired lease after a
    /// crash is enough. Implementations must absorb the resulting constraint violation and
    /// report <see cref="InboxWriteResult.AlreadyPresent"/>.
    /// </para>
    /// <para>
    /// <b>An exception from this method means a real failure</b> — a foreign-key or check
    /// violation, a lost connection, a timeout — and the caller should treat it as such.
    /// </para>
    /// <para>
    /// <b>On the transaction boundary.</b> With no ambient transaction this commits: there is no
    /// surrounding domain unit of work on the ingestion path (ADR-MSG-002), and EF opens an
    /// implicit transaction per save. Under an <b>ambient</b> transaction it does not commit —
    /// the caller owns the boundary and this method only contributes a statement to it.
    /// </para>
    /// <para>
    /// The result must not be discarded. Silent success is forbidden: a skip has to be returned,
    /// counted or logged, so that a rising deduplication rate — the signal of a lease set too
    /// short, or of a consumer that is stalling — stays visible.
    /// </para>
    /// <para>
    /// Implementations must use a <c>DbContext</c> dedicated to messaging. Committing here
    /// otherwise flushes every tracked domain change on the same context, and the savepoint
    /// rollback used to absorb a duplicate would then undo database writes that EF believes it
    /// has saved.
    /// </para>
    /// </remarks>
    ValueTask<InboxWriteResult> AddAsync(InboxMessage message, CancellationToken ct = default);
}
