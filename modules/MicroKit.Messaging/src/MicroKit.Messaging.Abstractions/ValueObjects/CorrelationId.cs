namespace MicroKit.Messaging;

/// <summary>
/// Strongly-typed correlation identifier that links causally related messages
/// across service boundaries.
/// </summary>
/// <param name="Value">The underlying <see cref="Guid"/> value.</param>
/// <remarks>
/// All messages in a single logical request chain share the same <see cref="CorrelationId"/>.
/// Pass it downstream when publishing integration events so the full chain can be traced.
/// </remarks>
public sealed record CorrelationId(Guid Value)
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

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
