using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using MicroKit.Messaging.Processing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;

namespace MicroKit.Messaging.UnitTests.Stores;

/// <summary>
/// The registration of a partitioned store (#191): its source is a singleton of its own type the engine
/// reaches through the store's key — the host's own when it registered one — and one store has one source.
/// </summary>
public sealed class PartitionedStoreCompositionTests
{
    private static readonly MessageStoreKey Key = new("tests:partitioned-composition");

    [Fact]
    public void PartitionedBy_RegistersTheSourceAsItsOwnSingleton_ThatTheStoresKeyReaches()
    {
        var services = new ServiceCollection();
        services.AddMicroKitMessaging(bus => bus.AddMessageStore(Key, s => s.PartitionedBy<Source, Initializer>()));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IMessageStoreInstanceSource>(Key)
            .ShouldBeSameAs(provider.GetRequiredService<Source>(), "the instance a host adds instances to is the one the engine reads");
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredKeyedService<IMessageStoreInstanceInitializer>(Key).ShouldBeOfType<Initializer>();
    }

    [Fact]
    public void PartitionedBy_KeepsASourceTheHostRegisteredFirst()
    {
        var mine = new Source();
        var services = new ServiceCollection();
        services.AddSingleton(mine);
        services.AddMicroKitMessaging(bus => bus.AddMessageStore(Key, s => s.PartitionedBy<Source, Initializer>()));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IMessageStoreInstanceSource>(Key).ShouldBeSameAs(mine);
    }

    [Fact]
    public void TheEngine_BuildsTheInboxInstruments_SoAPartitionedStoreWithNoInstanceReportsFromTheStart()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.partitioned-composition";
            bus.AddMessageStore(Key, s => s.PartitionedBy<Source, Initializer>());
        });
        using var provider = services.BuildServiceProvider();

        _ = provider.GetServices<IHostedService>().ToList();
        var meters = new ConcurrentQueue<Meter>();
        using (var listener = new MeterListener())
        {
            // Start reports every instrument that already exists; nothing has resolved InboxMetrics but the engine.
            // Another test's instrument can be published on its own thread meanwhile: hence the queue.
            listener.InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Name == "microkit.inbox.tenants.backoff")
                {
                    meters.Enqueue(instrument.Meter);
                }
            };
            listener.Start();
        }

        meters.ShouldContain(
            provider.GetRequiredService<InboxMetrics>().Meter,
            "the engine builds the inbox's instruments: a store with no instance yet reports zero, not nothing");
    }

    [Fact]
    public async Task AStoreGivenTwoSources_FailsAtStart()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.partitioned-composition";
            bus.AddMessageStore(Key, s => s.PartitionedBy<Source, Initializer>().PartitionedBy<Source, Initializer>());
        });
        using var host = builder.Build();

        var fault = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());
        fault.Message.ShouldContain($"Store '{Key}' is given an instance source 2 times");
    }

    private sealed class Source : IMessageStoreInstanceSource
    {
        public IReadOnlyCollection<MessageStoreInstance> GetInstances() => [];

        public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);

        public bool TryGetInstance(MessageContext context, [NotNullWhen(true)] out MessageStoreInstance? instance)
        {
            instance = null;
            return false;
        }
    }

    private sealed class Initializer : IMessageStoreInstanceInitializer
    {
        public ValueTask InitializeAsync(MessageStoreInstance instance, IServiceProvider services, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
