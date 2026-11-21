namespace MicroKit.Messaging.UnitTests.DI;

using Microsoft.Extensions.Hosting;

/// <summary>
/// D12: a store makes a host durable, not routable — a declared publication with no
/// <see cref="IMessageTransport"/> fails at start, naming the fix, rather than writing transport rows
/// the store's loop could never send.
/// </summary>
public sealed class NoTransportStartTests
{
    [MessageContract("tests.no-transport.event")]
    public sealed record NoTransportEvent(Guid Id) : IIntegrationEvent;

    [Fact]
    public async Task AStoreWithADeclaredPublication_AndNoTransport_FailsAtStart_NamingTheFix()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.no-transport";
            bus.Publishes<NoTransportEvent>();
            bus.AddMessageStore(new MessageStoreKey("tests:no-transport"), _ => { });
        });
        using var host = builder.Build();

        var fault = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        fault.Message.ShouldContain($"'{nameof(NoTransportEvent)}' is declared with bus.Publishes<T>(), but no IMessageTransport is registered");
        fault.Message.ShouldContain("bus.UseInMemoryTransport()");
    }
}
