namespace MicroKit.Messaging;

/// <summary>
/// Identifies one inbox row: a message as seen by one consumer.
/// </summary>
/// <remarks>
/// <para>
/// The inbox key is compound. The same <see cref="MessageId"/> fans out to one row per
/// consumer, and those rows advance independently — one handler may succeed while another
/// retries. Every settlement operation must therefore carry both halves; keying on
/// <see cref="MessageId"/> alone would settle a sibling consumer's row.
/// </para>
/// <para>
/// This is the <i>logical</i> key, used across the public API because callers reason in these
/// terms. Storage carries a single-column surrogate key underneath —
/// <see cref="InboxMessage.RowId"/> — which is what makes a batch claim expressible as one
/// bounded list rather than a cross product of two.
/// </para>
/// </remarks>
/// <param name="MessageId">The message identifier.</param>
/// <param name="ConsumerType">The consumer this row belongs to.</param>
public readonly record struct InboxMessageKey(MessageId MessageId, string ConsumerType);
