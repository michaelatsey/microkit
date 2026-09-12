namespace MicroKit.Messaging;

/// <summary>The outcome of recording one <see cref="MessageEnvelope"/> into the inbox.</summary>
/// <remarks>
/// <para>
/// One envelope produces one row per registered consumer, and those rows do not share a fate: a
/// redelivery may be absorbed for one consumer while another gets its first row. A bare count of
/// writes would report that as a partial failure, and a <see langword="bool"/> could not report it
/// at all — hence a tally rather than a verdict.
/// </para>
/// <para>
/// <b><see cref="ConsumersMatched"/> of zero is not an error, and is not silent either.</b> A
/// contract name this process understands with no <c>IMessageHandler&lt;T&gt;</c> registered for it
/// is a legitimate composition — a service may subscribe to a contract it merely relays or logs —
/// so nothing is written and nothing throws. But zero rows is otherwise indistinguishable from a
/// healthy delivery, which is the silent success this module treats as blocking, so the receiver
/// also logs at <c>Warning</c> and increments a counter. Read this member if you would rather fail
/// than warn.
/// </para>
/// <para>
/// Shaped like <see cref="IntegrationEventWriteResult"/> — private constructor, get-only members,
/// a static factory — deliberately, and <b>not</b> as a positional record. The freeze on
/// <see cref="MessageEnvelope"/>'s primary constructor exists because a provider <i>constructs</i>
/// one; this type travels the other way, built by <c>MicroKit.Messaging</c> and read by a provider,
/// so a private constructor lets it gain a member later without a source or binary break at any
/// call site.
/// <para>
/// <see cref="From"/> is nonetheless <b>public</b>, and that is a requirement rather than an
/// oversight: the only producer lives in <c>MicroKit.Messaging</c>, a different assembly, so the
/// factory cannot be internal. It is public for a cross-assembly caller, not as an invitation —
/// a provider reads this type and has nothing to gain by constructing one, and the guards on
/// <see cref="From"/> exist because "only we call it" is not something the compiler enforces.
/// </para>
/// </para>
/// <para>
/// <b><see cref="ConsumersMatched"/> is derived, and that is sound only because the receiver
/// refuses an unclassified write.</b> The sum holds as a count of consumers reached exactly while
/// every consumer lands in one of the two buckets below. <c>EnvelopeReceiver</c> guarantees that by
/// throwing on an <see cref="InboxWriteResult"/> it does not recognise rather than skipping it — a
/// skipped consumer would leave the sum silently short, which is the one way this member could
/// lie. A third disposition would therefore have to be counted here, not merely tolerated: the
/// coupling is load-bearing in both directions.
/// </para>
/// </remarks>
public sealed record EnvelopeReceiveResult
{
    private EnvelopeReceiveResult(int rowsAdded, int duplicates)
    {
        RowsAdded = rowsAdded;
        Duplicates = duplicates;
    }

    /// <summary>
    /// Gets the number of inbox rows this call inserted — first delivery of this message to that
    /// consumer.
    /// </summary>
    public int RowsAdded { get; }

    /// <summary>
    /// Gets the number of consumers that already held a row for this message, so nothing was
    /// written for them.
    /// </summary>
    /// <remarks>
    /// Normal under at-least-once delivery and never an error: one expired lease after a crash is
    /// enough to produce a redelivery. The <i>rate</i> is the signal — a sustained climb means a
    /// lease set too short, a stalling consumer, or a broker replaying.
    /// </remarks>
    public int Duplicates { get; }

    /// <summary>
    /// Gets the number of consumers registered for this contract, whether or not a row was written
    /// for each.
    /// </summary>
    public int ConsumersMatched => RowsAdded + Duplicates;

    /// <summary>Creates a result from one envelope's per-consumer tally.</summary>
    /// <param name="rowsAdded">Rows inserted. Zero or greater.</param>
    /// <param name="duplicates">
    /// Consumers whose row was already recorded. Zero or greater.
    /// </param>
    /// <returns>The tally for this envelope.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either argument is negative, which would make <see cref="ConsumersMatched"/> report fewer
    /// consumers than were reached — or a negative count of them. Both are counts of writes that
    /// happened, so neither can be below zero, and a result that misreports the fan-out is worse
    /// than no result: <see cref="ConsumersMatched"/> is documented as the member to read when a
    /// caller would rather fail than warn.
    /// </exception>
    public static EnvelopeReceiveResult From(int rowsAdded, int duplicates)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowsAdded);
        ArgumentOutOfRangeException.ThrowIfNegative(duplicates);

        return new(rowsAdded, duplicates);
    }
}
