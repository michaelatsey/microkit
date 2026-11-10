namespace MicroKit.Messaging;

/// <summary>
/// What a handler knows about the message it is handling, beyond the payload.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TenantId"/>, <see cref="CorrelationId"/> and <see cref="CausationId"/> are three
/// distinct attributes, each carried in its own envelope header, and none is ever derived from
/// another. <see cref="CausationId"/> is the cause of <i>this</i> message; a message published
/// while handling it takes <see cref="MessageId"/> as its own causation.
/// </para>
/// <para>
/// Members are init-only so a host can build one in its own handler tests.
/// </para>
/// </remarks>
public sealed class MessageContext
{
    /// <summary>Gets the message identifier assigned by the producer.</summary>
    public required MessageId MessageId { get; init; }

    /// <summary>Gets the contract name, without its version.</summary>
    public required string ContractName { get; init; }

    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }

    /// <summary>
    /// Gets the source the producer declared. Empty on the transitional inbox path, whose rows do
    /// not record it (#188).
    /// </summary>
    public required string Source { get; init; }

    /// <summary>Gets when the producer says the fact happened.</summary>
    public required DateTimeOffset OccurredOnUtc { get; init; }

    /// <summary>Gets the tenant the message belongs to. Null in a single-tenant deployment.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the correlation identifier of the chain this message belongs to.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Gets the identifier of the message that caused this one. Null for a root.</summary>
    public string? CausationId { get; init; }

    /// <summary>
    /// Gets every header the envelope carried, reserved ones included. Empty on the transitional
    /// inbox path, whose rows do not record headers (#188).
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
