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
/// </remarks>
public sealed record CausationId(Guid Value)
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

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
