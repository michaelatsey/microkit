namespace MicroKit.Messaging.Publishing;

/// <summary>
/// One module's contribution to the set of contracts this application understands.
/// </summary>
/// <remarks>
/// Registered once per module and composed into the single <see cref="IntegrationEventRegistry"/>
/// alongside every module's published contracts. Composing both kinds into one registry is what
/// makes a name collision <i>across</i> the two detectable at all: two separate structures could
/// not see that one type publishes a name a second type claims to consume, and the relay would
/// have to pick one arbitrarily.
/// </remarks>
public sealed class IntegrationEventSubscriptions
{
    internal IntegrationEventSubscriptions(IReadOnlyList<IntegrationEventSubscription> subscriptions)
        => Subscriptions = subscriptions;

    /// <summary>Gets the contracts this module understands.</summary>
    public IReadOnlyList<IntegrationEventSubscription> Subscriptions { get; }
}
