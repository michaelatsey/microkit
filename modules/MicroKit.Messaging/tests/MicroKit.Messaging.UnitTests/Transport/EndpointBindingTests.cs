namespace MicroKit.Messaging.UnitTests.Transport;

using MicroKit.Messaging.UnitTests.Bus;
using Microsoft.Extensions.Hosting;

/// <summary>
/// A host binds its in-memory endpoint on the first use of its transport or at host start,
/// whichever comes first (#187). Without the first-use binding, a single host would drop what it
/// publishes before it starts — the single queue of #186 held it. Each test is one host.
/// </summary>
public sealed class EndpointBindingTests
{
    private const string OrderPlacedWire = "tests.bus.order-placed.v1";

    [Fact]
    public async Task APublicationBeforeHostStart_ReachesTheHostsOwnHandlerOnceItStarts()
    {
        await using var host = BusTestHost.Build(
            bus => bus.AddHandler<FirstOrderPlacedHandler>(), null, null, "svc.seed", out var broker);

        // Seeding before RunAsync: the transport's first use binds the endpoint and holds the copy.
        var id = await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        broker.EndpointsBoundTo(OrderPlacedWire).ShouldBe(["svc.seed"]);
        host.Recorder.Deliveries.ShouldBeEmpty("nothing consumes before the host starts");

        await host.StartHostAsync();

        await Eventually.HoldsAsync(() => host.Recorder.Deliveries.Count == 1, "delivered after start");
        host.Recorder.Deliveries.Single().Context.MessageId.ShouldBe(id);
        host.DeadLetters.ShouldBeEmpty();
    }

    /// <summary>
    /// Registered before <c>AddMicroKitMessaging</c>, the publishing service's <c>StartAsync</c>
    /// runs before the consume loop's.
    /// </summary>
    [Fact]
    public async Task AHostedServicePublishingInItsStartAsync_ReachesTheHandler()
    {
        await using var host = await BusTestHost.StartAsync(
            bus => bus.AddHandler<FirstOrderPlacedHandler>(),
            services: services => services.AddHostedService<PublishOnStart>());

        await Eventually.HoldsAsync(() => host.Recorder.Deliveries.Count == 1, "the start-time publication was handled");
        host.DeadLetters.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnInvalidComposition_ResolvingTheTransportThrows_AndBindsNothing()
    {
        await using var host = BusTestHost.Build(
            bus => bus.AddHandler<FirstOrderPlacedHandler>(),
            null,
            services => services.AddScoped(_ => Substitute.For<IOutboxProcessorStore>()),
            "svc.invalid",
            out var broker);

        var ex = Should.Throw<IntegrationEventConfigurationException>(
            () => host.Provider.GetRequiredService<IMessageTransport>());
        ex.Message.ShouldContain("An IOutboxProcessorStore is registered, but the outbox engine is not");

        // The same exception reaches a publisher, and a failed attempt binds nothing either.
        await Should.ThrowAsync<IntegrationEventConfigurationException>(
            () => host.PublishAsync(new OrderPlaced(Guid.NewGuid())));
        broker.EndpointsBoundTo(OrderPlacedWire).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheBinding_IsIdempotent_AcrossFirstUseAndHostStart()
    {
        await using var host = BusTestHost.Build(
            bus => bus.AddHandler<FirstOrderPlacedHandler>(), null, null, "svc.once", out var broker);

        _ = host.Provider.GetRequiredService<IMessageTransport>();
        await host.StartHostAsync();

        broker.EndpointsBoundTo(OrderPlacedWire).ShouldBe(["svc.once"]);
    }

    private sealed class PublishOnStart(IServiceScopeFactory scopes) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                .PublishAsync(new OrderPlaced(Guid.NewGuid()), ct: cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
