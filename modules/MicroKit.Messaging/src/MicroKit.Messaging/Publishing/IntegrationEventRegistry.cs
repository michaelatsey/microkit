namespace MicroKit.Messaging.Publishing;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The integration contract surface of the service, in both directions: every type it publishes,
/// with its wire type; and every wire type it understands, with the local CLR type it deserializes
/// into. Composed once from the <see cref="MessagingComposition"/>, read-only afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built from the declarations, never by scanning.</b> Publications come from
/// <c>bus.Publishes&lt;T&gt;()</c>; the types understood are the publications plus every type a
/// registered handler handles — a handler is its subscription (#187). Publishing a contract also
/// binds its name, so a service consuming its own publications declares nothing twice.
/// </para>
/// <para>
/// <b>The reverse direction resolves a name to this process's own type</b>, never to the
/// producer's: the producer's CLR type does not travel, which is why a stable wire name exists.
/// </para>
/// <para>
/// <b>One local type per wire type.</b> The constructor applies
/// <see cref="MessagingCompositionAnalysis"/> and throws on any problem, so a container resolved
/// without a host rejects the compositions a host start rejects, with the same messages.
/// </para>
/// <para>
/// <b>It carries each contract's name and version</b> (#187), read from the attribute at
/// registration, so the receive paths build a message's context without reflection.
/// </para>
/// </remarks>
internal sealed class IntegrationEventRegistry
{
    private readonly Dictionary<Type, IntegrationEventRegistration> _published = [];
    private readonly Dictionary<Type, IntegrationEventRegistration> _known = [];
    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _subscribedNames = new(StringComparer.Ordinal);

    /// <summary>Composes the registry from the declared publications and handlers.</summary>
    /// <param name="composition">The declarations.</param>
    /// <param name="serviceName">The service name: the source of every publication.</param>
    /// <exception cref="IntegrationEventConfigurationException">
    /// The composition breaks a contract rule (see <see cref="MessagingCompositionAnalysis"/>).
    /// </exception>
    public IntegrationEventRegistry(MessagingComposition composition, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        MessagingCompositionAnalysis.ThrowIfAny(MessagingCompositionAnalysis.Analyze(composition));

        Source = serviceName;

        foreach (var type in composition.Publications)
        {
            _published[type] = Know(composition, type);
        }

        foreach (var handled in composition.Handled)
        {
            _subscribedNames.Add(Know(composition, handled.MessageType).ContractName);
        }
    }

    /// <summary>Gets the source of every publication: the service name.</summary>
    public string Source { get; }

    /// <summary>
    /// Gets every wire type this service <b>publishes</b>, ordered. Snapshot-testable: a contract
    /// name is public API.
    /// </summary>
    public IReadOnlyList<string> ContractNames =>
        [.. _published.Values.Select(r => r.ContractName).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Gets every wire type a registered handler of this service <b>handles</b>, ordered — the
    /// contracts the service's endpoint subscribes to.
    /// </summary>
    public IReadOnlyList<string> SubscribedContractNames => [.. _subscribedNames];

    /// <summary>Gets the number of published contracts.</summary>
    public int Count => _published.Count;

    /// <summary>Resolves the contract a type is published under.</summary>
    /// <param name="eventType">The runtime type of the message being published.</param>
    /// <returns>The registration for <paramref name="eventType"/>.</returns>
    /// <exception cref="IntegrationEventConfigurationException">The type is not published.</exception>
    /// <remarks>
    /// There is deliberately no <c>TryResolveContract</c>: a miss here is a programming error —
    /// code holding an instance it never declared — while a miss in the reverse direction is data
    /// off the wire.
    /// </remarks>
    public IntegrationEventRegistration ResolveContract(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return _published.TryGetValue(eventType, out var registration)
            ? registration
            : throw new IntegrationEventConfigurationException(
                $"'{eventType.FullName}' is not a published integration event. Add " +
                $"bus.Publishes<{eventType.Name}>() inside AddMicroKitMessaging. Publications are " +
                "declared so a service's contracts stay reviewable and checked at start.");
    }

    /// <summary>
    /// Gets the contract of any type this registry knows — published or handled. The receive paths
    /// read a message's contract name and version from here.
    /// </summary>
    /// <param name="eventType">The local type.</param>
    /// <param name="registration">Its contract, when the type is known.</param>
    /// <returns><see langword="true"/> when the type is known.</returns>
    public bool TryGetContract(Type eventType, [NotNullWhen(true)] out IntegrationEventRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return _known.TryGetValue(eventType, out registration);
    }

    /// <summary>Attempts to resolve the local type a wire type deserializes into.</summary>
    /// <param name="contractName">The wire type, as received.</param>
    /// <param name="eventType">The local type bound to it.</param>
    /// <returns><see langword="true"/> when the wire type is bound in this process.</returns>
    /// <remarks>
    /// Matching is <b>ordinal</b>: a wire type is an identity, and a case-insensitive match would
    /// collapse two contracts onto one type.
    /// </remarks>
    public bool TryResolveLocalType(string contractName, [NotNullWhen(true)] out Type? eventType)
    {
        ArgumentNullException.ThrowIfNull(contractName);
        return _byName.TryGetValue(contractName, out eventType);
    }

    /// <summary>Resolves the local type a wire type deserializes into.</summary>
    /// <param name="contractName">The wire type, as received.</param>
    /// <returns>The local type bound to <paramref name="contractName"/>.</returns>
    /// <exception cref="IntegrationEventConfigurationException">No local type is bound to it.</exception>
    /// <remarks>
    /// An unbound wire type is permanent until a redeploy; a caller draining a queue classifies it
    /// as such. Use <see cref="TryResolveLocalType"/> where the miss is expected.
    /// </remarks>
    public Type ResolveLocalType(string contractName)
    {
        ArgumentNullException.ThrowIfNull(contractName);

        return _byName.TryGetValue(contractName, out var eventType)
            ? eventType
            : throw new IntegrationEventConfigurationException(
                $"Wire type '{contractName}' resolves to no local type. Register a handler of a " +
                "type carrying the matching [MessageContract] — bus.AddHandler<THandler>() — or " +
                "publish it with bus.Publishes<T>().");
    }

    private IntegrationEventRegistration Know(MessagingComposition composition, Type type)
    {
        if (_known.TryGetValue(type, out var known))
        {
            return known;
        }

        // The analysis above has already rejected a declared type with no contract.
        var contract = composition.ContractOf(type)!;
        var registration = new IntegrationEventRegistration(type, contract.Name, contract.Version);
        _known[type] = registration;
        _byName[registration.ContractName] = type;
        return registration;
    }
}
