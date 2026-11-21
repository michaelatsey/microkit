using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// V7 (#188 D10, #128): a host stopped mid-batch releases every claimed row — the one in flight
/// and those not yet reached — with its attempt count unchanged. A host stop is never a message
/// failure.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class ShutdownReleaseTests(PostgreSqlFixture fixture)
{
    [DockerRequiredFact]
    public async Task AHostStoppedMidOutboxBatch_ReleasesEveryClaimedRowWithItsAttemptCountUnchanged()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var transport = new BlockingTransport();
        using var host = BuildHost(db, transport, configure: bus =>
        {
            bus.Publishes<ShutdownEvent>();
            bus.Services.AddSingleton<IMessageTransport>(transport);
            bus.AddTransportDispatcher();
        });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            for (var i = 0; i < 3; i++)
            {
                await bus.PublishAsync(new ShutdownEvent(Guid.NewGuid()));
            }

            await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
        }

        await host.StartAsync();
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (await ReadOutboxAsync(db)).ShouldAllBe(r => r.Status == OutboxMessageStatus.Processing, "the batch is claimed");

        await host.StopAsync();

        var rows = await ReadOutboxAsync(db);
        rows.Count.ShouldBe(3);
        rows.ShouldAllBe(r => r.Status == OutboxMessageStatus.Pending, "released, not retried");
        rows.ShouldAllBe(r => r.RetryCount == 0, "a host stop charges no attempt");
        rows.ShouldAllBe(r => r.ClaimToken == null && r.LockedUntilUtc == null && r.NextRetryAtUtc == null);
        rows.ShouldAllBe(r => !r.DeadLettered);
    }

    [DockerRequiredFact]
    public async Task AHostStoppedMidInboxBatch_ReleasesEveryClaimedRowWithItsAttemptCountUnchanged()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var gate = new HandlerGate();
        using var host = BuildHost(db, transport: null, configure: bus =>
        {
            bus.Services.AddSingleton(gate);
            bus.AddHandler<BlockingShutdownHandler>();
        });

        await using (var context = NewContext(db))
        {
            for (var i = 0; i < 3; i++)
            {
                context.InboxMessages.Add(new InboxMessage
                {
                    MessageId = MessageId.New(),
                    ConsumerType = MessageConsumerKey.Of(typeof(BlockingShutdownHandler)),
                    EventType = typeof(ShutdownEvent).AssemblyQualifiedName!,
                    Payload = $"{{\"id\":\"{Guid.NewGuid()}\"}}",
                    Status = InboxMessageStatus.Failed,
                    RetryCount = 1,
                    ReceivedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-i),
                    OccurredOnUtc = DateTimeOffset.UtcNow,
                });
            }

            await context.SaveChangesAsync();
        }

        await host.StartAsync();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (await ReadInboxAsync(db)).ShouldAllBe(r => r.Status == InboxMessageStatus.Processing, "the batch is claimed");

        await host.StopAsync();

        var rows = await ReadInboxAsync(db);
        rows.Count.ShouldBe(3);
        rows.ShouldAllBe(r => r.Status == InboxMessageStatus.Failed, "released back to the retry loop, not retried");
        rows.ShouldAllBe(r => r.RetryCount == 1, "the attempt count is the one the rows had");
        rows.ShouldAllBe(r => r.ClaimToken == null && r.LockedUntilUtc == null && r.NextRetryAtUtc == null);
        rows.ShouldAllBe(r => !r.DeadLettered);
    }

    private static IHost BuildHost(StoreDatabase db, BlockingTransport? transport, Action<MessagingBuilder> configure)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddDbContext<TestMessagingDbContext>(o => o.UseNpgsql(db.ConnectionString));
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.shutdown";
            bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
            bus.Configure(o =>
            {
                o.Outbox.PollingInterval = TimeSpan.FromMilliseconds(50);
                o.Inbox.PollingInterval = TimeSpan.FromMilliseconds(50);
                o.Outbox.RetentionDays = 0;
                o.Inbox.RetentionDays = 0;
            });
            configure(bus);
        });
        return builder.Build();
    }

    private static TestMessagingDbContext NewContext(StoreDatabase db)
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(db.ConnectionString).Options);

    private static async Task<List<OutboxMessage>> ReadOutboxAsync(StoreDatabase db)
    {
        await using var context = NewContext(db);
        return await context.OutboxMessages.AsNoTracking().ToListAsync();
    }

    private static async Task<List<InboxMessage>> ReadInboxAsync(StoreDatabase db)
    {
        await using var context = NewContext(db);
        return await context.InboxMessages.AsNoTracking().ToListAsync();
    }

    /// <summary>A transport whose send never completes until the host stops.</summary>
    private sealed class BlockingTransport : IMessageTransport
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }
    }
}

[MessageContract("tests.shutdown.event")]
public sealed record ShutdownEvent(Guid Id) : IIntegrationEvent;

public sealed class HandlerGate
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>A handler that never completes until the host stops.</summary>
public sealed class BlockingShutdownHandler(HandlerGate gate) : IMessageHandler<ShutdownEvent>
{
    public async ValueTask HandleAsync(ShutdownEvent message, MessageContext context, CancellationToken ct = default)
    {
        gate.Entered.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
    }
}
