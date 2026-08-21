namespace MicroKit.Messaging;

/// <summary>
/// Strongly-typed identifier for a message in the outbox or inbox.
/// </summary>
/// <param name="Value">The underlying <see cref="Guid"/> value.</param>
/// <remarks>
/// Create via <see cref="New()"/> for a fresh identifier or <see cref="From(Guid)"/>
/// to wrap an existing value. Never use <c>new MessageId(Guid.Empty)</c> in production code.
/// <para>
/// Implements <see cref="IComparable{T}"/> so that a collection of identifiers can be sorted.
/// The outbox claim relies on this: it sorts its candidate identifiers before stamping them, so
/// that concurrent processors whose candidate sets intersect lock the overlapping rows in the
/// same order and cannot deadlock. A positional record does not derive an ordering, so without
/// this interface <c>List&lt;MessageId&gt;.Sort()</c> throws for any batch of two or more —
/// while silently succeeding for zero or one.
/// </para>
/// </remarks>
public sealed record MessageId(Guid Value) : IComparable<MessageId>
{
    /// <summary>
    /// Creates a new <see cref="MessageId"/> backed by a newly generated <see cref="Guid"/>.
    /// </summary>
    /// <returns>A new unique <see cref="MessageId"/>.</returns>
    public static MessageId New() => new(Guid.NewGuid());

    /// <summary>
    /// Wraps an existing <see cref="Guid"/> in a <see cref="MessageId"/>.
    /// </summary>
    /// <param name="value">The Guid to wrap.</param>
    /// <returns>A <see cref="MessageId"/> wrapping <paramref name="value"/>.</returns>
    public static MessageId From(Guid value) => new(value);

    /// <summary>Compares this identifier with another by underlying <see cref="Guid"/> value.</summary>
    /// <param name="other">The identifier to compare against. May be <see langword="null"/>.</param>
    /// <returns>
    /// A negative value, zero, or a positive value depending on relative order.
    /// <see langword="null"/> sorts before any instance, matching the framework convention.
    /// </returns>
    public int CompareTo(MessageId? other) => other is null ? 1 : Value.CompareTo(other.Value);

    /// <summary>Determines whether one identifier sorts before another.</summary>
    /// <param name="left">The left operand. May be <see langword="null"/>.</param>
    /// <param name="right">The right operand. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> sorts before <paramref name="right"/>.</returns>
    public static bool operator <(MessageId? left, MessageId? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    /// <summary>Determines whether one identifier sorts before another or is equal to it.</summary>
    /// <param name="left">The left operand. May be <see langword="null"/>.</param>
    /// <param name="right">The right operand. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> does not sort after <paramref name="right"/>.</returns>
    public static bool operator <=(MessageId? left, MessageId? right) =>
        left is null || left.CompareTo(right) <= 0;

    /// <summary>Determines whether one identifier sorts after another.</summary>
    /// <param name="left">The left operand. May be <see langword="null"/>.</param>
    /// <param name="right">The right operand. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> sorts after <paramref name="right"/>.</returns>
    public static bool operator >(MessageId? left, MessageId? right) =>
        left is not null && left.CompareTo(right) > 0;

    /// <summary>Determines whether one identifier sorts after another or is equal to it.</summary>
    /// <param name="left">The left operand. May be <see langword="null"/>.</param>
    /// <param name="right">The right operand. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> does not sort before <paramref name="right"/>.</returns>
    public static bool operator >=(MessageId? left, MessageId? right) =>
        left is null ? right is null : left.CompareTo(right) >= 0;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
