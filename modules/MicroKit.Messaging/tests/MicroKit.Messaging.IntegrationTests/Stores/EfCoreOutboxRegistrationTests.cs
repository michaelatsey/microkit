namespace MicroKit.Messaging.IntegrationTests.Stores;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Durability is opt-in (#188): a store registers the one engine that drains every store, and
/// its services keyed by its key. With no store, no engine.
/// </summary>
public sealed class EfCoreOutboxRegistrationTests
{
    private static readonly MessageStoreKey Key = EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>();

    [Fact]
    public void UseEntityFrameworkOutbox_RegistersTheEngineOnce_AndTheStoreServicesUnderItsKey()
    {
        var services = new ServiceCollection();

        services.AddMicroKitMessaging(bus => bus
            .UseEntityFrameworkOutbox<TestMessagingDbContext>()
            .UseEntityFrameworkOutbox<TestMessagingDbContext>()
            .UseEntityFrameworkOutbox<SecondMessagingDbContext>());

        services.Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType!.Name)
            .ShouldBe(["MessagingCompositionValidator", "MessageStoreEngine"], ignoreOrder: true);

        foreach (var contract in new[]
        {
            typeof(IOutboxWriter), typeof(IOutboxProcessorStore), typeof(IOutboxAdminStore),
            typeof(IOutboxRetentionStore), typeof(IInboxWriter), typeof(IInboxProcessorStore),
            typeof(IInboxSettlementStore), typeof(IInboxAdminStore), typeof(IInboxRetentionStore),
        })
        {
            services.Count(d => d.ServiceType == contract && d.IsKeyedService && Key.Equals(d.ServiceKey))
                .ShouldBe(1, $"{contract.Name} is registered once under the store's key, a second call adding nothing");
            services.ShouldNotContain(
                d => d.ServiceType == contract && !d.IsKeyedService,
                $"{contract.Name} has no unkeyed registration: a second DbContext cannot overwrite the first");
        }

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageStoreSet>().GetStores()
            .ShouldBe([Key, EntityFrameworkOutbox.KeyOf<SecondMessagingDbContext>()]);
    }

    [Fact]
    public void WithoutAStore_NoEngineIsRegistered()
    {
        var services = new ServiceCollection();

        services.AddMicroKitMessaging(bus => bus.UseInMemoryTransport());

        services.Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType?.Name)
            .ShouldBe(["MessagingCompositionValidator", "InMemoryTransportWorker"], ignoreOrder: true);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageStoreSet>().GetStores().ShouldBeEmpty();
    }
}
