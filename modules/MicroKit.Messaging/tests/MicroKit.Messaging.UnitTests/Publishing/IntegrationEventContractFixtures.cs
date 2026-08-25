namespace MicroKit.Messaging.UnitTests.Publishing;

using MicroKit.Messaging.Publishing;

/// <summary>
/// Builds contract contributions the way an application does — through
/// <c>AddIntegrationEventContracts</c> and <c>AddIntegrationEventSubscriptions</c> — so the tests
/// exercise the real composition path rather than a shape only they can produce.
/// </summary>
internal static class IntegrationEventContractFixtures
{
    internal static IntegrationEventContracts Contracts(
        string source, Action<IntegrationEventContractBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddIntegrationEventContracts(source, configure);

        return services.BuildServiceProvider().GetRequiredService<IntegrationEventContracts>();
    }

    internal static IntegrationEventSubscriptions Subscriptions(
        Action<IntegrationEventSubscriptionBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddIntegrationEventSubscriptions(configure);

        return services.BuildServiceProvider().GetRequiredService<IntegrationEventSubscriptions>();
    }

    internal static IntegrationEventRegistry Registry(
        params (string Source, Action<IntegrationEventContractBuilder> Configure)[] modules)
        => new([.. modules.Select(m => Contracts(m.Source, m.Configure))], []);

    /// <summary>Composes both kinds of contribution, as an application that does both would.</summary>
    internal static IntegrationEventRegistry MixedRegistry(
        (string Source, Action<IntegrationEventContractBuilder> Configure)[] modules,
        params Action<IntegrationEventSubscriptionBuilder>[] consumers)
        => new(
            [.. modules.Select(m => Contracts(m.Source, m.Configure))],
            [.. consumers.Select(Subscriptions)]);

    /// <summary>Composes subscriptions alone, as a service that only consumes would.</summary>
    internal static IntegrationEventRegistry ConsumingRegistry(
        params Action<IntegrationEventSubscriptionBuilder>[] consumers)
        => new([], [.. consumers.Select(Subscriptions)]);
}

[IntegrationEvent("saasbtp.safety.constat-recorded.v1")]
internal sealed record ConstatRecorded(Guid ConstatId) : IIntegrationEvent;

[IntegrationEvent("saasbtp.access.workspace-created.v1")]
internal sealed record WorkspaceCreated(Guid WorkspaceId) : IIntegrationEvent;

/// <summary>Claims a contract name that already belongs to <see cref="ConstatRecorded"/>.</summary>
[IntegrationEvent("saasbtp.safety.constat-recorded.v1")]
internal sealed record ImpostorEvent(Guid Whatever) : IIntegrationEvent;

/// <summary>
/// A contract emitted by another service. This process understands it and never publishes it —
/// the case that only exists because consumption is registered separately from publication.
/// </summary>
[IntegrationEvent("partner.billing.invoice-settled.v1")]
internal sealed record InvoiceSettled(Guid InvoiceId) : IIntegrationEvent;

/// <summary>Carries no <c>[IntegrationEvent]</c> attribute, deliberately.</summary>
internal sealed record NeverRegistered : IIntegrationEvent;

/// <summary>
/// Carries a wire identity that is not one. The attribute applies and compiles; it throws when the
/// attribute is materialized, which is inside the registration call that names this type.
/// </summary>
[IntegrationEvent("")]
internal sealed record BlankContractName : IIntegrationEvent;

/// <summary>Whitespace is the same defect wearing a disguise the compiler will not strip.</summary>
[IntegrationEvent("   ")]
internal sealed record WhitespaceContractName : IIntegrationEvent;
