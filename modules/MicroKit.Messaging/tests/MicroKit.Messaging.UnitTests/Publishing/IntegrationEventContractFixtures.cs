namespace MicroKit.Messaging.UnitTests.Publishing;

using MicroKit.Messaging.Publishing;

/// <summary>
/// Builds contract contributions the way an application does — through
/// <c>AddIntegrationEventContracts</c> — so the tests exercise the real composition path rather
/// than a shape only they can produce.
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

    internal static IntegrationEventRegistry Registry(
        params (string Source, Action<IntegrationEventContractBuilder> Configure)[] modules)
        => new([.. modules.Select(m => Contracts(m.Source, m.Configure))]);
}

[IntegrationEvent("saasbtp.safety.constat-recorded.v1")]
internal sealed record ConstatRecorded(Guid ConstatId) : IIntegrationEvent;

[IntegrationEvent("saasbtp.access.workspace-created.v1")]
internal sealed record WorkspaceCreated(Guid WorkspaceId) : IIntegrationEvent;

/// <summary>Claims a contract name that already belongs to <see cref="ConstatRecorded"/>.</summary>
[IntegrationEvent("saasbtp.safety.constat-recorded.v1")]
internal sealed record ImpostorEvent(Guid Whatever) : IIntegrationEvent;

/// <summary>Carries no <c>[IntegrationEvent]</c> attribute, deliberately.</summary>
internal sealed record NeverRegistered : IIntegrationEvent;
