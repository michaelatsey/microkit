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

    /// <summary>
    /// #124 V9 and C1: a host stopped while a tenant is in back-off — so mid-way through the second pass
    /// of the reordered batch — releases every row it had not attempted, whichever pass it belongs to,
    /// with its attempt count unchanged.
    /// </summary>
    [DockerRequiredFact]
    public async Task AHostStoppedDuringTheSecondPassOfAnOutboxBatch_ReleasesEveryUnattemptedRowWithItsAttemptCountUnchanged()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var transport = new TenantBlockingTransport("A");
        using var host = BuildHost(db, transport: null, configure: bus =>
        {
            bus.Services.AddSingleton<IMessageTransport>(transport);
            bus.AddTransportDispatcher();
        });

        // A failed a minute ago: its back-off is over, its entry is not — its rows go in the second pass.
        var store = EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>();
        host.Services.GetRequiredKeyedService<OutboxEndpointBackoff>(store)
            .TenantsOf(OutboxDispatchers.Transport).OnDown("A", DateTimeOffset.UtcNow.AddMinutes(-1), out _);

        var a1 = OutboxRow("A", -3, retryCount: 2);
        var a2 = OutboxRow("A", -2, retryCount: 2);
        var c1 = OutboxRow("C", -1, retryCount: 0);
        await using (var seed = NewContext(db))
        {
            seed.OutboxMessages.AddRange(a1, a2, c1);
            await seed.SaveChangesAsync();
        }

        await host.StartAsync();
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        transport.Sent.ShouldBe(["C"], "control: the first pass ran, the host stops in the second");

        await host.StopAsync();

        var rows = (await ReadOutboxAsync(db)).ToDictionary(r => r.Id);
        rows[c1.Id].Status.ShouldBe(OutboxMessageStatus.Published, "the row attempted before the stop keeps its outcome");
        foreach (var id in new[] { a1.Id, a2.Id })
        {
            rows[id].Status.ShouldBe(OutboxMessageStatus.Pending, "released, not retried");
            rows[id].RetryCount.ShouldBe(2, "a host stop charges no attempt");
            rows[id].ClaimToken.ShouldBeNull();
            rows[id].LockedUntilUtc.ShouldBeNull();
            rows[id].DeadLettered.ShouldBeFalse();
        }
    }

    /// <summary>The inbox side of the test above (#124 V9, C1).</summary>
    [DockerRequiredFact]
    public async Task AHostStoppedDuringTheSecondPassOfAnInboxBatch_ReleasesEveryUnattemptedRowWithItsAttemptCountUnchanged()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var gate = new TenantGate("A");
        using var host = BuildHost(db, transport: null, configure: bus =>
        {
            bus.Services.AddSingleton(gate);
            bus.AddHandler<TenantBlockingHandler>();
        });

        host.Services.GetRequiredService<InboxBackoffGroup>()
            .For(EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>())
            .OnDown(MessageConsumerKey.Of(typeof(TenantBlockingHandler)), "A", DateTimeOffset.UtcNow.AddMinutes(-1), out _);

        var a1 = InboxRow("A", -3, retryCount: 2);
        var a2 = InboxRow("A", -2, retryCount: 2);
        var c1 = InboxRow("C", -1, retryCount: 1);
        await using (var seed = NewContext(db))
        {
            seed.InboxMessages.AddRange(a1, a2, c1);
            await seed.SaveChangesAsync();
        }

        await host.StartAsync();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        gate.Handled.ShouldBe(["C"], "control: the first pass ran, the host stops in the second");

        await host.StopAsync();

        var rows = (await ReadInboxAsync(db)).ToDictionary(r => r.RowId);
        rows[c1.RowId].Status.ShouldBe(InboxMessageStatus.Processed);
        foreach (var id in new[] { a1.RowId, a2.RowId })
        {
            rows[id].Status.ShouldBe(InboxMessageStatus.Failed, "released back to the retry loop, not retried");
            rows[id].RetryCount.ShouldBe(2, "the attempt count is the one the rows had");
            rows[id].ClaimToken.ShouldBeNull();
            rows[id].LockedUntilUtc.ShouldBeNull();
            rows[id].DeadLettered.ShouldBeFalse();
        }
    }

    private static OutboxMessage OutboxRow(string tenant, int secondsAgo, int retryCount) => new()
    {
        Id = MessageId.New(),
        ContractName = "tests.shutdown.event.v1",
        Source = "tests.shutdown",
        TenantId = tenant,
        EventType = typeof(ShutdownEvent).AssemblyQualifiedName!,
        Payload = $"{{\"id\":\"{Guid.NewGuid()}\"}}",
        Status = OutboxMessageStatus.Pending,
        RetryCount = retryCount,
        OccurredOnUtc = DateTimeOffset.UtcNow,
        CreatedAtUtc = DateTimeOffset.UtcNow.AddSeconds(secondsAgo - 60),
        CorrelationId = CorrelationId.New(),
    };

    private static InboxMessage InboxRow(string tenant, int secondsAgo, int retryCount) => new()
    {
        MessageId = MessageId.New(),
        ConsumerType = MessageConsumerKey.Of(typeof(TenantBlockingHandler)),
        TenantId = tenant,
        EventType = typeof(ShutdownEvent).AssemblyQualifiedName!,
        Payload = $"{{\"id\":\"{Guid.NewGuid()}\"}}",
        Status = InboxMessageStatus.Failed,
        RetryCount = retryCount,
        ReceivedAtUtc = DateTimeOffset.UtcNow.AddSeconds(secondsAgo - 60),
        OccurredOnUtc = DateTimeOffset.UtcNow,
    };

    /// <summary>Sends every tenant's envelope but one, which it holds until the host stops.</summary>
    private sealed class TenantBlockingTransport(string blocked) : IMessageTransport
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string?> Sent { get; } = [];

        public async ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
        {
            var tenant = envelope.Headers.GetValueOrDefault(MessageHeaders.TenantId);
            if (tenant == blocked)
            {
                Entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }

            lock (Sent)
            {
                Sent.Add(tenant);
            }
        }
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

/// <summary>Holds one tenant's handler until the host stops; records the others.</summary>
public sealed class TenantGate(string blocked)
{
    public string Blocked { get; } = blocked;

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string?> Handled { get; } = [];
}

public sealed class TenantBlockingHandler(TenantGate gate) : IMessageHandler<ShutdownEvent>
{
    public async ValueTask HandleAsync(ShutdownEvent message, MessageContext context, CancellationToken ct = default)
    {
        if (context.TenantId == gate.Blocked)
        {
            gate.Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }

        lock (gate.Handled)
        {
            gate.Handled.Add(context.TenantId);
        }
    }
}
