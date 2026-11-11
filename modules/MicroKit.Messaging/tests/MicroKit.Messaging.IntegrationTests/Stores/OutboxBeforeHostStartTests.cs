namespace MicroKit.Messaging.IntegrationTests.Stores;

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// One host, a real EF outbox on SQLite, and the in-memory transport (#187): a contract row staged
/// before the host starts is dispatched by the outbox worker once it starts, and reaches the host's
/// own handler through its endpoint — bound at start, before the outbox worker runs.
/// </summary>
public sealed class OutboxBeforeHostStartTests
{
    [Fact]
    public async Task AnOutboxRowStagedBeforeHostStart_IsDeliveredOnceTheHostStarts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var received = new ConcurrentQueue<Guid>();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSingleton(received);
        builder.Services.AddDbContext<TestMessagingDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.outbox-before-start";
            bus.Publishes<StagedBeforeStart>();
            bus.AddHandler<StagedBeforeStartHandler>();
            bus.AddEfCoreOutbox<TestMessagingDbContext>();
            bus.AddEfCoreIntegrationEvents<TestMessagingDbContext>();
            bus.UseInMemoryTransport();
            bus.Configure(o =>
            {
                o.Outbox.PollingInterval = TimeSpan.FromMilliseconds(50);
                o.Outbox.MaxPollingInterval = TimeSpan.FromMilliseconds(100);
            });
        });
        using var host = builder.Build();

        var id = Guid.NewGuid();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();
            await context.Database.EnsureCreatedAsync();

            await using var transaction = await context.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                .PublishAsync(new StagedBeforeStart(id));
            await transaction.CommitAsync();
        }

        await host.StartAsync();
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (received.IsEmpty && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            received.ToArray().ShouldBe([id], "the staged row reached the handler, once");

            await using var check = host.Services.CreateAsyncScope();
            var row = await check.ServiceProvider.GetRequiredService<TestMessagingDbContext>()
                .OutboxMessages.SingleAsync();
            row.Status.ShouldBe(OutboxMessageStatus.Published);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}

[MessageContract("tests.outbox.staged-before-start")]
internal sealed record StagedBeforeStart(Guid Id) : IIntegrationEvent;

internal sealed class StagedBeforeStartHandler(ConcurrentQueue<Guid> received) : IMessageHandler<StagedBeforeStart>
{
    public ValueTask HandleAsync(StagedBeforeStart message, MessageContext context, CancellationToken ct = default)
    {
        received.Enqueue(message.Id);
        return ValueTask.CompletedTask;
    }
}
