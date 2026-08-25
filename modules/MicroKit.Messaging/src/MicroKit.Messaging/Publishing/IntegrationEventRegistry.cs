namespace MicroKit.Messaging.Publishing;

/// <summary>
/// The published contract surface of the application: every integration event that may be emitted,
/// with its wire name and its emitting module.
/// </summary>
/// <remarks>
/// <para>
/// Having an actual list buys two cheap tests: every <see cref="IIntegrationEvent"/> in a module is
/// registered — a forgotten one fails the build instead of failing at runtime on the single path
/// that emits it — and <see cref="ContractNames"/> is snapshot-tested, so an accidental rename
/// fails, which is the correct behaviour for a public contract.
/// </para>
/// <para>
/// <b>Publishing only.</b> There is no name-to-type lookup here. A publisher needs
/// <c>Type to name</c>; the reverse belongs to a consumer, which maps a name to <i>its own</i>
/// handler. Assuming the producer's CLR type is useful to a consumer is the very thing a stable
/// contract name exists to avoid.
/// </para>
/// <para>
/// Singleton, composed once from every module's contribution and read-only afterwards.
/// </para>
/// </remarks>
public sealed class IntegrationEventRegistry
{
    private readonly Dictionary<Type, IntegrationEventRegistration> _byType;

    /// <summary>Composes every module's contracts into one registry, rejecting collisions.</summary>
    /// <param name="contributions">Every module's declared contracts.</param>
    /// <exception cref="IntegrationEventConfigurationException">
    /// Two modules declare the same contract name, or one type is declared twice.
    /// </exception>
    /// <remarks>
    /// Composing all modules into a single registry is what makes a cross-module name collision
    /// detectable at all. With one registry per module — the shape a per-module options object
    /// would have produced — two modules could publish different payloads under one name and
    /// nothing would notice until a consumer failed to deserialize.
    /// </remarks>
    public IntegrationEventRegistry(IEnumerable<IntegrationEventContracts> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);

        _byType = [];
        var owners = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var registration in contributions.SelectMany(c => c.Registrations))
        {
            if (_byType.TryGetValue(registration.EventType, out var existing))
            {
                throw new IntegrationEventConfigurationException(
                    $"'{registration.EventType.FullName}' is declared twice: as " +
                    $"'{existing.ContractName}' from '{existing.Source}' and as " +
                    $"'{registration.ContractName}' from '{registration.Source}'. One type has one " +
                    "contract.");
            }

            if (owners.TryGetValue(registration.ContractName, out var owner))
            {
                throw new IntegrationEventConfigurationException(
                    $"Contract name '{registration.ContractName}' is declared by both " +
                    $"'{owner.FullName}' and '{registration.EventType.FullName}'. Two types " +
                    "publishing under one name are indistinguishable on the wire, and a consumer " +
                    "would deserialize whichever it happened to receive into the wrong shape.");
            }

            _byType[registration.EventType] = registration;
            owners[registration.ContractName] = registration.EventType;
        }
    }

    /// <summary>Gets every declared contract name, ordered. The snapshot test asserts on this.</summary>
    public IReadOnlyList<string> ContractNames =>
        [.. _byType.Values.Select(r => r.ContractName).Order(StringComparer.Ordinal)];

    /// <summary>Gets the number of declared contracts.</summary>
    public int Count => _byType.Count;

    /// <summary>Resolves the contract an event type is published under.</summary>
    /// <param name="eventType">The runtime type of the event being published.</param>
    /// <returns>The registration for <paramref name="eventType"/>.</returns>
    /// <exception cref="IntegrationEventConfigurationException">The type is not registered.</exception>
    public IntegrationEventRegistration Resolve(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return _byType.TryGetValue(eventType, out var registration)
            ? registration
            : throw new IntegrationEventConfigurationException(
                $"'{eventType.FullName}' is not a registered integration event. Add " +
                $"events.Publishes<{eventType.Name}>() to the module's composition root. " +
                "Registration is explicit so a module's published contracts stay reviewable.");
    }
}
