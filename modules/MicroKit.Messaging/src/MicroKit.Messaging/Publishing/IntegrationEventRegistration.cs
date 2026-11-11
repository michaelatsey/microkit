namespace MicroKit.Messaging.Publishing;

using System.Globalization;

/// <summary>
/// One local message type and its contract: the name and the version read from its
/// <see cref="MessageContractAttribute"/> at registration (#187).
/// </summary>
/// <param name="EventType">The local CLR type.</param>
/// <param name="Name">The contract name, without its version suffix.</param>
/// <param name="Version">The contract version, at least 1.</param>
/// <remarks>
/// The registry carries the contract so the receive paths read it from here, never from the
/// attribute by reflection on each delivery. It holds no source: the source is the service name,
/// one per service, on the registry.
/// </remarks>
internal sealed record IntegrationEventRegistration(Type EventType, string Name, int Version)
{
    /// <summary>Gets the wire type, <c>&lt;name&gt;.v&lt;version&gt;</c>, the envelope's <c>Type</c>.</summary>
    public string ContractName { get; } =
        string.Create(CultureInfo.InvariantCulture, $"{Name}.v{Version}");
}
