namespace MicroKit.Messaging;

/// <summary>
/// Strongly-typed correlation identifier that links causally related messages
/// across service boundaries.
/// </summary>
/// <param name="Value">The underlying <see cref="Guid"/> value.</param>
/// <remarks>
/// All messages in a single logical request chain share the same <see cref="CorrelationId"/>.
/// Pass it downstream when publishing integration events so the full chain can be traced.
/// <para>
/// Implements <see cref="IComparable{T}"/> for symmetry with <see cref="MessageId"/>, so that
/// the three messaging identifiers order consistently. See <see cref="MessageId"/> for why an
/// ordering is required at all.
/// </para>
/// </remarks>
public sealed record CorrelationId(Guid Value) : IComparable<CorrelationId>
{
    /// <summary>
    /// Creates a new <see cref="CorrelationId"/> backed by a newly generated <see cref="Guid"/>.
    /// Use this to start a new correlation chain (e.g., at the entry point of a request).
    /// </summary>
    /// <returns>A new unique <see cref="CorrelationId"/>.</returns>
    public static CorrelationId New() => new(Guid.NewGuid());

    /// <summary>
    /// Wraps an existing <see cref="Guid"/> in a <see cref="CorrelationId"/>.
    /// Use this to propagate an existing correlation identifier from an upstream message.
    /// </summary>
    /// <param name="value">The Guid to wrap.</param>
    /// <returns>A <see cref="CorrelationId"/> wrapping <paramref name="value"/>.</returns>
    public static CorrelationId From(Guid value) => new(value);

    /// <summary>Compares this identifier with another by underlying <see cref="Guid"/> value.</summary>
    /// <param name="other">The identifier to compare against. May be <see langword="null"/>.</param>
    /// <returns>
    /// A negative value, zero, or a positive value depending on relative order.
    /// <see langword="null"/> sorts before any instance, matching the framework convention.
    /// </returns>
    public int CompareTo(CorrelationId? other) => other is null ? 1 : Value.CompareTo(other.Value);

    /// <summary>Determines whether one identifier sorts before another.</summary>
    /// <param name="left">The left operand. May be <see langword="null"/>.</param>
    /// <param name="right">The right operand. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> sorts before <paramref name="right"/>.</returns>
    public static bool operator <(CorrelationId? left, CorrelationId? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    /// <summary>Determines whether one identifier sorts before another or is equal to it.</summary>
    /// <param name="left">The left operand. May be <see langword="null"/>.</param>
    /// <param name="right">The right operand. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> does not sort after <paramref name="right"/>.</returns>
    public static bool operator <=(CorrelationId? left, CorrelationId? right) =>
        left is null || left.CompareTo(right) <= 0;

    /// <summary>Determines whether one identifier sorts after another.</summary>
    /// <param name="left">The left operand. May be <see langword="null"/>.</param>
    /// <param name="right">The right operand. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> sorts after <paramref name="right"/>.</returns>
    public static bool operator >(CorrelationId? left, CorrelationId? right) =>
        left is not null && left.CompareTo(right) > 0;

    /// <summary>Determines whether one identifier sorts after another or is equal to it.</summary>
    /// <param name="left">The left operand. May be <see langword="null"/>.</param>
    /// <param name="right">The right operand. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> does not sort before <paramref name="right"/>.</returns>
    public static bool operator >=(CorrelationId? left, CorrelationId? right) =>
        left is null ? right is null : left.CompareTo(right) >= 0;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
