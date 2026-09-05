namespace MicroKit.Messaging;

/// <summary>The outcome of staging one integration event into the outbox.</summary>
/// <remarks>
/// <para>
/// Like <see cref="InboxWriteResult"/>, this exists because the most important outcome —
/// "already published, nothing written" — is <b>not a failure</b>. A redelivered dispatch re-runs
/// its notification handlers, which publish the same contract from the same origin row; the second
/// write collides on <c>UX_OutboxMessages_Origin_ContractName</c>. That is the nominal path the
/// replay key was designed for, and reporting it as an exception would push an infrastructure
/// concern into every notification handler.
/// </para>
/// <para>
/// It carries an <see cref="Id"/> rather than being a bare enum because the two paths yield
/// <i>different</i> identifiers, and the caller needs whichever is real. On the absorbed path the
/// row this call would have written does not exist, so returning its id would name nothing; the id
/// returned is the <b>existing</b> row's, which is what a consumer's inbox will actually
/// deduplicate on and what a handler logging its publication should record on both attempts.
/// </para>
/// <para>
/// <see cref="AlreadyPublished"/> is a bool rather than an enum member, unlike
/// <see cref="InboxWriteResult"/>. The objection recorded there is to a bare <c>bool</c>
/// <i>return value</i>, where a reader must recall which way round <c>if (!inserted)</c> runs. A
/// named property on a result type states its own polarity: <c>if (result.AlreadyPublished)</c>.
/// </para>
/// </remarks>
public sealed record IntegrationEventWriteResult
{
    private IntegrationEventWriteResult(MessageId id, bool alreadyPublished)
    {
        Id = id;
        AlreadyPublished = alreadyPublished;
    }

    /// <summary>
    /// Gets the identifier of the outbox row that carries this publication — the one written by
    /// this call, or the one already there.
    /// </summary>
    /// <remarks>
    /// Either way this is the value stamped into <see cref="MessageEnvelope.MessageId"/> when the
    /// row is dispatched, and therefore the key a consumer's inbox deduplicates on.
    /// </remarks>
    public MessageId Id { get; }

    /// <summary>
    /// Gets a value indicating whether this contract was already staged from the same origin row,
    /// so nothing was written.
    /// </summary>
    /// <remarks>
    /// Normal under at-least-once delivery and never an error. The caller must treat it as a
    /// successful publication, not as a failed one — the event is already in the queue.
    /// </remarks>
    public bool AlreadyPublished { get; }

    /// <summary>The row was written. First publication of this contract from this origin.</summary>
    /// <param name="id">The identifier of the row just staged.</param>
    /// <returns>A result naming the newly staged row.</returns>
    public static IntegrationEventWriteResult Staged(MessageId id) => new(id, alreadyPublished: false);

    /// <summary>The replay key held: this contract is already staged from this origin.</summary>
    /// <param name="id">The identifier of the row that is already there.</param>
    /// <returns>A result naming the pre-existing row.</returns>
    public static IntegrationEventWriteResult AlreadyPublishedAs(MessageId id)
        => new(id, alreadyPublished: true);
}
