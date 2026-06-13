namespace MicroKit.Messaging;

/// <summary>
/// Strongly-typed identifier for a message in the outbox or inbox.
/// </summary>
/// <param name="Value">The underlying <see cref="Guid"/> value.</param>
/// <remarks>
/// Create via <see cref="New()"/> for a fresh identifier or <see cref="From(Guid)"/>
/// to wrap an existing value. Never use <c>new MessageId(Guid.Empty)</c> in production code.
/// </remarks>
public sealed record MessageId(Guid Value)
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

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
