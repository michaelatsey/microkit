namespace MicroKit.Messaging.IntegrationTests.Stores;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Since #186 the store provider, not <c>AddMicroKitMessaging()</c>, is what registers the outbox
/// and inbox workers: they require the stores it registers.
/// </summary>
public sealed class EfCoreOutboxRegistrationTests
{
    [Fact]
    public void AddEfCoreOutbox_RegistersTheFourOutboxAndInboxWorkers()
    {
        var services = new ServiceCollection();

        services.AddMicroKitMessaging(bus => bus.AddEfCoreOutbox<TestMessagingDbContext>());

        services.Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType!.Name)
            .ShouldBe(
                ["MessagingCompositionValidator", "OutboxWorker", "OutboxRetentionWorker", "InboxWorker", "InboxRetentionWorker"],
                ignoreOrder: true);
    }

    [Fact]
    public void WithoutAStoreProvider_NoOutboxOrInboxWorkerIsRegistered()
    {
        var services = new ServiceCollection();

        services.AddMicroKitMessaging(bus => bus.UseInMemoryTransport());

        services.Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType?.Name)
            .ShouldNotContain(name => name != null && name.EndsWith("Worker", StringComparison.Ordinal)
                && name != "InMemoryTransportWorker");
    }
}
