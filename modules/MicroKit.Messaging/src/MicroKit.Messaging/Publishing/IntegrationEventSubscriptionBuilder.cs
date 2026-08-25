namespace MicroKit.Messaging.Publishing;

using System.Reflection;

/// <summary>Declares the integration events one module understands.</summary>
/// <remarks>
/// <para>
/// <b>Explicit registration, not assembly scanning</b> — the same reasoning as
/// <see cref="IntegrationEventContractBuilder"/>. Scanning produces the same mapping with nobody
/// able to read it, and what a module consumes is as much a reviewable part of its surface as what
/// it publishes.
/// </para>
/// <para>
/// <b>Declaring consumption is a different act from declaring publication</b>, which is why this is
/// a separate builder reached through a separate call. A producer says "I emit this contract, under
/// this name, from this source"; a consumer says "I understand this contract name, as this local
/// type" — and has no source to declare.
/// </para>
/// <para>
/// A module needs this only for a contract it does <i>not</i> publish itself. Publishing a contract
/// already binds its name to the declaring type, so a modular monolith routes its own contracts
/// with no second declaration; declaring both is accepted as a no-op rather than rejected, so a
/// module never has to know whether its dependency happens to be in-process.
/// </para>
/// </remarks>
public sealed class IntegrationEventSubscriptionBuilder
{
    private readonly List<IntegrationEventSubscription> _subscriptions = [];

    internal IntegrationEventSubscriptionBuilder()
    {
    }

    /// <summary>Declares that this module understands <typeparamref name="TEvent"/>.</summary>
    /// <typeparam name="TEvent">
    /// The local event type. Must carry <see cref="IntegrationEventAttribute"/> bearing the same
    /// contract name the producer publishes under — that attribute is what binds a wire name to a
    /// local type.
    /// </typeparam>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="IntegrationEventConfigurationException">
    /// <typeparamref name="TEvent"/> has no <see cref="IntegrationEventAttribute"/>. Rejected here,
    /// at registration, rather than on the first message that arrives under that name.
    /// </exception>
    public IntegrationEventSubscriptionBuilder Consumes<TEvent>()
        where TEvent : IIntegrationEvent
    {
        var eventType = typeof(TEvent);
        var attribute = eventType.GetCustomAttribute<IntegrationEventAttribute>(inherit: false)
            ?? throw new IntegrationEventConfigurationException(
                $"'{eventType.FullName}' has no [IntegrationEvent] attribute. A consumed event " +
                "declares the contract name it answers to there — the same name the producer " +
                "publishes under. Without it nothing binds the name arriving on the wire to this " +
                "local type, and the CLR type name cannot serve: the producer holds a different " +
                "type in a different assembly.");

        _subscriptions.Add(new IntegrationEventSubscription(eventType, attribute.ContractName));

        return this;
    }

    internal IntegrationEventSubscriptions Build() => new(_subscriptions);
}
