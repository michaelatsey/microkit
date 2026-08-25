namespace MicroKit.Messaging;

/// <summary>
/// Declares the wire contract name an <see cref="IIntegrationEvent"/> is published under.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mandatory on every published event.</b> The assembly-qualified CLR type name is not usable
/// as a wire identity: a namespace rename invalidates rows already in flight, and a consumer in
/// another service holds a different type in a different assembly, so the name could never match.
/// A stable contract name is what lets a module be extracted into its own service without any
/// consumer noticing.
/// </para>
/// <para>
/// Absence is a startup failure, not a runtime one — <c>IntegrationEventContractBuilder.Publishes</c>
/// rejects a type without this attribute at registration.
/// </para>
/// <para>
/// <b>Read on both sides.</b> A producer declares the name it emits under; a consuming module puts
/// the <i>same</i> name on its own local type, and that attribute is the whole of what binds an
/// arriving wire name to a CLR type it can deserialize into — the producer's type does not travel,
/// and the consumer does not hold its assembly.
/// <c>IntegrationEventSubscriptionBuilder.Consumes</c> reads it the same way and rejects its
/// absence at registration too. It follows that within one process a name resolves to exactly one
/// local type: two types carrying one name is rejected at composition, because a message is
/// deserialized once and an ambiguous name could only be resolved by guessing.
/// </para>
/// <para>
/// <b>Versioning is additive-only.</b> An optional field keeps the name; anything a consumer could
/// break on takes a new name and a period of dual publication. The version suffix is part of the
/// name for that reason.
/// </para>
/// <code>
/// [IntegrationEvent("saasbtp.safety.constat-recorded.v1")]
/// public sealed record ConstatRecorded(Guid ConstatId, Guid SiteId) : IIntegrationEvent;
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class IntegrationEventAttribute : Attribute
{
    /// <summary>Declares the wire contract name this event is published under.</summary>
    /// <param name="contractName">
    /// The wire contract name, e.g. <c>saasbtp.safety.constat-recorded.v1</c>. This is public API:
    /// renaming it strands every consumer subscribed to the old name.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="contractName"/> is null, empty, or whitespace.
    /// </exception>
    /// <remarks>
    /// The guard is here rather than in the builders because both of them read this attribute, so
    /// one check covers publication and consumption alike — and it runs the moment the attribute is
    /// materialized, which is inside the registration call that names the offending type.
    /// <b>The empty name is the case that matters</b>, more than the null one: null fails somewhere
    /// regardless, whereas an empty name is a perfectly usable dictionary key. Unguarded it binds,
    /// resolves, and travels — an event published under no wire identity at all, which a consumer
    /// can only match by having made the same mistake.
    /// </remarks>
    public IntegrationEventAttribute(string contractName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);

        ContractName = contractName;
    }

    /// <summary>Gets the wire contract name this event is published under.</summary>
    public string ContractName { get; }
}
