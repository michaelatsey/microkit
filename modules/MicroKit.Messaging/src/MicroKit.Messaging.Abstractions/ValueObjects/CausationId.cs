namespace MicroKit.Messaging;

/// <summary>
/// Strongly-typed causation identifier that records which message directly caused
/// the current message to be published.
/// </summary>
/// <param name="Value">The underlying <see cref="Guid"/> value.</param>
/// <remarks>
/// Set to the <see cref="MessageId.Value"/> of the inbound message that triggered
/// this outbound message. <see langword="null"/> on root events (events originating from
/// a user command with no prior event in the chain).
/// <para>
/// Implements <see cref="IComparable{T}"/> for symmetry with <see cref="MessageId"/>, so that
/// the three messaging identifiers order consistently. See <see cref="MessageId"/> for why an
/// ordering is required at all.
/// </para>
/// </remarks>
public sealed record CausationId(Guid Value) : IComparable<CausationId>
{
    /// <summary>
    /// Creates a new <see cref="CausationId"/> backed by a newly generated <see cref="Guid"/>.
    /// </summary>
    /// <returns>A new unique <see cref="CausationId"/>.</returns>
    public static CausationId New() => new(Guid.NewGuid());

    /// <summary>
    /// Wraps an existing <see cref="Guid"/> in a <see cref="CausationId"/>.
    /// Typically the <see cref="MessageId.Value"/> of the triggering message.
    /// </summary>
    /// <param name="value">The Guid to wrap.</param>
    /// <returns>A <see cref="CausationId"/> wrapping <paramref name="value"/>.</returns>
    public static CausationId From(Guid value) => new(value);

    /// <summary>Compares this identifier with another by underlying <see cref="Guid"/> value.</summary>
    /// <param name="other">The identifier to compare against. May be <see langword="null"/>.</param>
    /// <returns>
    /// A negative value, zero, or a positive value depending on relative order.
    /// <see langword="null"/> sorts before any instance, matching the framework convention.
    /// </returns>
    public int CompareTo(CausationId? other) => other is null ? 1 : Value.CompareTo(other.Value);

    /// <summary>Determines whether one identifier sorts before another.</summary>
    /// <param name="left">The left operand. May be <see langword="null"/>.</param>
    /// <param name="right">The right operand. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> sorts before <paramref name="right"/>.</returns>
    public static bool operator <(CausationId? left, CausationId? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    /// <summary>Determines whether one identifier sorts before another or is equal to it.</summary>
    /// <param name="left">The left operand. May be <see langword="null"/>.</param>
    /// <param name="right">The right operand. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> does not sort after <paramref name="right"/>.</returns>
    public static bool operator <=(CausationId? left, CausationId? right) =>
        left is null || left.CompareTo(right) <= 0;

    /// <summary>Determines whether one identifier sorts after another.</summary>
    /// <param name="left">The left operand. May be <see langword="null"/>.</param>
    /// <param name="right">The right operand. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> sorts after <paramref name="right"/>.</returns>
    public static bool operator >(CausationId? left, CausationId? right) =>
        left is not null && left.CompareTo(right) > 0;

    /// <summary>Determines whether one identifier sorts after another or is equal to it.</summary>
    /// <param name="left">The left operand. May be <see langword="null"/>.</param>
    /// <param name="right">The right operand. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> does not sort before <paramref name="right"/>.</returns>
    public static bool operator >=(CausationId? left, CausationId? right) =>
        left is null ? right is null : left.CompareTo(right) >= 0;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
