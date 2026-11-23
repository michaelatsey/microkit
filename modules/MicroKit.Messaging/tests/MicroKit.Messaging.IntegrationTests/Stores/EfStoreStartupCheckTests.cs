using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MicroKit.Messaging.IntegrationTests.Stores;

/// <summary>
/// D12 (#188), the EF store's own start checks: its <c>DbContext</c> must be registered, and its
/// model must carry the messaging entities. Each fails the start with its fix.
/// </summary>
public sealed class EfStoreStartupCheckTests
{
    [Fact]
    public async Task AStoreWhoseDbContextIsNotRegistered_FailsAtStart()
    {
        using var host = Build(services => { });

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        ex.Message.ShouldContain($"is the outbox of '{typeof(TestMessagingDbContext).FullName}', which is not registered");
        ex.Message.ShouldContain("services.AddDbContext<TestMessagingDbContext>(...)");
    }

    [Fact]
    public async Task AStoreWhoseModelLacksTheMessagingEntities_FailsAtStart()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        using var host = Build(services => services.AddDbContext<BareContext>(o => o.UseSqlite(connection)), bare: true);

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(() => host.StartAsync());

        ex.Message.ShouldContain("has no OutboxMessage or InboxMessage entity");
        ex.Message.ShouldContain("modelBuilder.ApplyMessagingConfiguration()");
    }

    [Fact]
    public async Task AStoreWithARegisteredContextAndTheEntities_Starts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        using var host = Build(services => services.AddDbContext<TestMessagingDbContext>(o => o.UseSqlite(connection)));

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public void ASingleStoresCheck_RunsOnAContainerWithoutTheKeyedServiceQuery()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestMessagingDbContext>(o => o.UseSqlite(connection));
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.ef-checks";
            bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
        });
        using var provider = services.BuildServiceProvider();
        var composition = (MessagingComposition)services
            .Single(d => d.ServiceType == typeof(MessagingComposition)).ImplementationInstance!;
        var container = new WithoutKeyedServiceQuery(provider);

        var problems = composition.Stores.SelectMany(s => s.StartupChecks).SelectMany(check => check(container)).ToList();

        problems.ShouldBeEmpty("a single store's check needs no IServiceProviderIsKeyedService (#191)");
    }

    private static IHost Build(Action<IServiceCollection> services, bool bare = false)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        services(builder.Services);
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.ef-checks";
            if (bare)
            {
                bus.UseEntityFrameworkOutbox<BareContext>();
            }
            else
            {
                bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
            }

            bus.Configure(o =>
            {
                o.Outbox.RetentionDays = 0;
                o.Inbox.RetentionDays = 0;
            });
        });
        return builder.Build();
    }

    /// <summary>A context whose model forgot <c>ApplyMessagingConfiguration()</c>.</summary>
    private sealed class BareContext(DbContextOptions<BareContext> options) : DbContext(options)
    {
        public DbSet<BusinessRecord> Records => Set<BusinessRecord>();
    }

    /// <summary>A container that resolves keyed services but cannot be asked whether one is registered.</summary>
    private sealed class WithoutKeyedServiceQuery(ServiceProvider inner) : IServiceProvider, IKeyedServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IServiceProviderIsKeyedService) ? null : inner.GetService(serviceType);

        public object? GetKeyedService(Type serviceType, object? serviceKey) => inner.GetKeyedService(serviceType, serviceKey);

        public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
            => inner.GetRequiredKeyedService(serviceType, serviceKey);
    }
}
