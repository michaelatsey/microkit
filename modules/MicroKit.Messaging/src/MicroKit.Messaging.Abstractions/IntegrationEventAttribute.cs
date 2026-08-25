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
/// <b>Versioning is additive-only.</b> An optional field keeps the name; anything a consumer could
/// break on takes a new name and a period of dual publication. The version suffix is part of the
/// name for that reason.
/// </para>
/// <code>
/// [IntegrationEvent("saasbtp.safety.constat-recorded.v1")]
/// public sealed record ConstatRecorded(Guid ConstatId, Guid SiteId) : IIntegrationEvent;
/// </code>
/// </remarks>
/// <param name="contractName">
/// The wire contract name, e.g. <c>saasbtp.safety.constat-recorded.v1</c>. This is public API:
/// renaming it strands every consumer subscribed to the old name.
/// </param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class IntegrationEventAttribute(string contractName) : Attribute
{
    /// <summary>Gets the wire contract name this event is published under.</summary>
    public string ContractName { get; } = contractName;
}
