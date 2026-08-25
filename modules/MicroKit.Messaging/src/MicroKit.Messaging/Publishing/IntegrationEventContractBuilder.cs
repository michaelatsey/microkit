namespace MicroKit.Messaging.Publishing;

using System.Reflection;

/// <summary>Declares the integration events one module publishes.</summary>
/// <remarks>
/// <b>Explicit registration, not assembly scanning.</b> Scanning produces the same mapping with
/// nobody able to read it, and a module's published contracts are exactly the thing that should be
/// readable — in one place, in review, in a diff. It is the module's public API in the same sense
/// its HTTP routes are.
/// </remarks>
public sealed class IntegrationEventContractBuilder
{
    private readonly string _source;
    private readonly List<IntegrationEventRegistration> _registrations = [];

    internal IntegrationEventContractBuilder(string source) => _source = source;

    /// <summary>Declares that this module publishes <typeparamref name="TEvent"/>.</summary>
    /// <typeparam name="TEvent">
    /// The event type. Must carry <see cref="IntegrationEventAttribute"/>.
    /// </typeparam>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="IntegrationEventConfigurationException">
    /// <typeparamref name="TEvent"/> has no <see cref="IntegrationEventAttribute"/>. Rejected here,
    /// at registration, rather than on the one code path that emits the event.
    /// </exception>
    public IntegrationEventContractBuilder Publishes<TEvent>()
        where TEvent : IIntegrationEvent
    {
        var eventType = typeof(TEvent);
        var attribute = eventType.GetCustomAttribute<IntegrationEventAttribute>(inherit: false)
            ?? throw new IntegrationEventConfigurationException(
                $"'{eventType.FullName}' has no [IntegrationEvent] attribute. Every published event " +
                "declares its wire contract name there: the CLR type name cannot serve as one, " +
                "because a consumer in another service holds a different type in a different " +
                "assembly and a namespace rename would invalidate rows already in flight.");

        _registrations.Add(
            new IntegrationEventRegistration(eventType, attribute.ContractName, _source));

        return this;
    }

    internal IntegrationEventContracts Build() => new(_source, _registrations);
}
