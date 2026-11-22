using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MicroKit.Messaging.IntegrationTests.PostgreSql;
using MicroKit.Messaging.IntegrationTests.PostgreSql.Instances;

namespace MicroKit.Messaging.IntegrationTests.Readme;

/// <summary>
/// V10 (#191): a host built as the README shows a partitioned store, under Development's
/// <c>ValidateOnBuild</c> and <c>ValidateScopes</c>, starts and drains a tenant's database.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class PartitionedHostShapeTests(InstanceServerFixture server) : IClassFixture<InstanceServerFixture>
{
    [DockerRequiredFact]
    public async Task TheReadmesPartitionedStore_StartsAndDrains_UnderDevelopmentValidation()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            ApplicationName = "tests.partitioned-readme",
        });
        ReadmeSnippets.PartitionedComposition(builder.Services);
        builder.Services.AddSingleton<ReadmeRecorder>();
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.Publishes<ReadmeOrderPlaced>();
            bus.AddHandler<ReadmeOrderPlacedHandler>();
            bus.UseInMemoryTransport();
        });

        using var host = builder.Build();
        var tenant = await server.CreateDatabaseAsync();
        host.Services.GetRequiredService<TenantDatabases>().Add("tenant-a", tenant);
        await host.StartAsync();
        try
        {
            // The host's own code points its scope at the tenant's database, as the initializer does
            // for the engine's scopes.
            var orderId = Guid.NewGuid();
            await using (var scope = host.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<TenantConnection>().ConnectionString = tenant;
                await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new ReadmeOrderPlaced(orderId), new PublishOptions { TenantId = "tenant-a" });
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
            }

            var recorder = host.Services.GetRequiredService<ReadmeRecorder>();
            await Eventually.HoldsAsync(() => recorder.Handled.Contains(orderId), "the tenant's row is sent and handled");

            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(tenant).Options);
            await Eventually.HoldsAsync(
                async () => await db.Set<OutboxMessage>().AsNoTracking().AllAsync(r => r.Status == OutboxMessageStatus.Published),
                "the row is settled in the tenant's database");
            (await db.Set<InboxMessage>().AsNoTracking().CountAsync(r => r.Status == InboxMessageStatus.Processed))
                .ShouldBe(1, "the delivery's inbox row is in the tenant's database");
        }
        finally
        {
            await host.StopAsync();
        }
    }
}

public sealed class ReadmeRecorder
{
    public System.Collections.Concurrent.ConcurrentBag<Guid> Handled { get; } = [];
}

[MessageContract("tests.readme.order-placed")]
public sealed record ReadmeOrderPlaced(Guid OrderId) : IIntegrationEvent;

public sealed class ReadmeOrderPlacedHandler(ReadmeRecorder recorder) : IMessageHandler<ReadmeOrderPlaced>
{
    public ValueTask HandleAsync(ReadmeOrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Handled.Add(message.OrderId);
        return ValueTask.CompletedTask;
    }
}
