using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// #188 D6c makes a retrying execution strategy a supported configuration, and both stores must
/// settle under it. A retrying strategy refuses a transaction begun outside it: a settlement
/// that does so throws, the processor swallows it, and every row of the batch is claimed again
/// after each lease expiry, forever. Under the strategy, a commit whose outcome is unknown is
/// verified rather than replayed blind, and a fault that outlasts the retries surfaces itself.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class SettlementStrategyTests(PostgreSqlFixture fixture)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMilliseconds(300);
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Under <c>EnableRetryOnFailure</c>, a batch of two — one sent, one failing out of its single
    /// attempt — is settled: the first <c>Published</c>, the second <c>Failed</c> with its attempt
    /// counted. Once the lease has expired, another pass sends neither again.
    /// </summary>
    [DockerRequiredFact]
    public async Task UnderEnableRetryOnFailure_AnOutboxBatchIsSettled_AndNothingIsResentAfterTheLease()
    {
        await using var db = await NewDatabase();
        var transport = new CountingTransport();
        var logs = new CapturingLoggerProvider();
        // The host's clock is driven: "after the lease" is a step of this clock, not a wall-clock wait
        // that a backward step of the wall clock could cut short.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var host = BuildHost(db, transport, logs, clock);

        var sent = new SettleEvent(Guid.NewGuid());
        var failing = new SettleEvent(Guid.NewGuid());
        transport.Failing.TryAdd(failing.Id, 0);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(sent);
            await bus.PublishAsync(failing);
            await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
        }

        var first = await ProcessOnceAsync(host);

        first.Published.ShouldBe(1);
        first.DeadLettered.ShouldBe(1);
        logs.Entries.ShouldNotContain(e => e.EventId == 1008, "the settlement was not refused by the strategy");
        var rows = await ReadOutboxAsync(db);
        var published = rows.Single(r => r.Payload.Contains(sent.Id.ToString(), StringComparison.Ordinal));
        published.Status.ShouldBe(OutboxMessageStatus.Published);
        published.ClaimToken.ShouldBeNull();
        var deadLettered = rows.Single(r => r.Payload.Contains(failing.Id.ToString(), StringComparison.Ordinal));
        deadLettered.Status.ShouldBe(OutboxMessageStatus.Failed);
        deadLettered.DeadLettered.ShouldBeTrue();
        deadLettered.RetryCount.ShouldBe(1, "the failed attempt is counted");
        deadLettered.ClaimToken.ShouldBeNull();

        published.LockedUntilUtc.ShouldBeNull("settled: no lease left");
        deadLettered.LockedUntilUtc.ShouldBeNull("settled: no lease left");

        // Past every lease the first batch stamped, by the host's own clock.
        clock.Advance(Lease + TimeSpan.FromSeconds(1));
        var second = await ProcessOnceAsync(host);

        second.Claimed.ShouldBe(0, "a settled row is never claimed again");
        transport.Sends(sent.Id).ShouldBe(1, "sent once, not after every lease expiry");
        transport.Sends(failing.Id).ShouldBe(1);
    }

    /// <summary>
    /// The outbox settlement commits, then the commit faults: its outcome looks unknown. The
    /// verification finds no row still carrying the token and returns the count that commit wrote.
    /// A blind replay writes nothing (the token is cleared) and reports a settled batch as lost
    /// leases.
    /// </summary>
    [DockerRequiredFact]
    public async Task OutboxSettlement_WhenItsCommitLandsAndThenFaults_IsVerified_AndReportsTheCountItWrote()
    {
        await using var db = await NewDatabase();
        var ids = await SeedOutboxAsync(db, count: 2);
        var faults = new CommitFaultInjector();
        await using var context = RetryingContext(db, faults);
        var store = new EfOutboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
        var claim = await store.ClaimBatchAsync(10, TimeSpan.FromMinutes(5), OutboxEndpoints.None);
        claim.Messages.Count.ShouldBe(2);

        faults.Arm(CommitFaultPoint.AfterCommit);
        var written = await store.ApplyOutcomesAsync(claim.Token, [.. ids.Select(OutboxOutcome.Published)]);

        faults.Fired.ShouldBeTrue();
        written.ShouldBe(2, "the commit landed: its count, not a replay's zero");
        (await ReadOutboxAsync(db)).ShouldAllBe(r => r.Status == OutboxMessageStatus.Published && r.ClaimToken == null);
    }

    /// <summary>
    /// A commit fault that outlasts the strategy's retries reaches the caller as itself, not as the
    /// strategy's <c>RetryLimitExceededException</c>, and nothing is settled.
    /// </summary>
    [DockerRequiredFact]
    public async Task OutboxSettlement_WhenTheFaultOutlastsTheRetries_PropagatesTheFaultItself()
    {
        await using var db = await NewDatabase();
        var ids = await SeedOutboxAsync(db, count: 2);
        var faults = new CommitFaultInjector();
        await using var context = RetryingContext(db, faults);
        var store = new EfOutboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
        var claim = await store.ClaimBatchAsync(10, TimeSpan.FromMinutes(5), OutboxEndpoints.None);

        faults.Arm(CommitFaultPoint.BeforeCommit, times: 10);
        await Should.ThrowAsync<InjectedCommitFaultException>(
            async () => await store.ApplyOutcomesAsync(claim.Token, [.. ids.Select(OutboxOutcome.Published)]));

        (await ReadOutboxAsync(db)).ShouldAllBe(r => r.Status == OutboxMessageStatus.Processing && r.ClaimToken == claim.Token);
    }

    /// <summary>The inbox twin of the outbox case: a commit that landed and then faulted is verified.</summary>
    [DockerRequiredFact]
    public async Task InboxSettlement_WhenItsCommitLandsAndThenFaults_IsVerified_AndReportsTheCountItWrote()
    {
        await using var db = await NewDatabase();
        var keys = await SeedInboxAsync(db, count: 2);
        var faults = new CommitFaultInjector();
        await using var context = RetryingContext(db, faults);
        var store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
        var claim = await store.ClaimBatchAsync(10, TimeSpan.FromMinutes(5));
        claim.Messages.Count.ShouldBe(2);

        faults.Arm(CommitFaultPoint.AfterCommit);
        var written = await store.ApplyOutcomesAsync(claim.Token, [.. keys.Select(InboxOutcome.Released)]);

        faults.Fired.ShouldBeTrue();
        written.ShouldBe(2, "the commit landed: its count, not a replay's zero");
        (await ReadInboxAsync(db)).ShouldAllBe(r => r.Status == InboxMessageStatus.Failed && r.ClaimToken == null);
    }

    /// <summary>The inbox twin: a fault that outlasts the retries surfaces itself, and nothing is settled.</summary>
    [DockerRequiredFact]
    public async Task InboxSettlement_WhenTheFaultOutlastsTheRetries_PropagatesTheFaultItself()
    {
        await using var db = await NewDatabase();
        var keys = await SeedInboxAsync(db, count: 2);
        var faults = new CommitFaultInjector();
        await using var context = RetryingContext(db, faults);
        var store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
        var claim = await store.ClaimBatchAsync(10, TimeSpan.FromMinutes(5));

        faults.Arm(CommitFaultPoint.BeforeCommit, times: 10);
        await Should.ThrowAsync<InjectedCommitFaultException>(
            async () => await store.ApplyOutcomesAsync(claim.Token, [.. keys.Select(InboxOutcome.Released)]));

        (await ReadInboxAsync(db)).ShouldAllBe(r => r.Status == InboxMessageStatus.Processing && r.ClaimToken == claim.Token);
    }

    private Task<StoreDatabase> NewDatabase()
        => StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));

    private static TestMessagingDbContext RetryingContext(StoreDatabase db, CommitFaultInjector faults)
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>()
            .UseNpgsql(db.ConnectionString, npgsql => npgsql.ExecutionStrategy(d => new FaultRetryingStrategy(d)))
            .AddInterceptors(faults)
            .Options);

    private static TestMessagingDbContext PlainContext(StoreDatabase db)
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(db.ConnectionString).Options);

    private static async Task<List<MessageId>> SeedOutboxAsync(StoreDatabase db, int count)
    {
        var rows = Enumerable.Range(0, count).Select(_ => new OutboxMessage
        {
            Id = MessageId.New(),
            TenantId = "tenant-a",
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Status = OutboxMessageStatus.Pending,
            OccurredOnUtc = Now,
            CreatedAtUtc = Now,
            CorrelationId = CorrelationId.New(),
        }).ToList();

        await using var context = PlainContext(db);
        context.OutboxMessages.AddRange(rows);
        await context.SaveChangesAsync();
        return rows.ConvertAll(r => r.Id);
    }

    private static async Task<List<InboxMessageKey>> SeedInboxAsync(StoreDatabase db, int count)
    {
        var rows = Enumerable.Range(0, count).Select(i => new InboxMessage
        {
            RowId = Guid.NewGuid(),
            MessageId = MessageId.New(),
            ConsumerType = $"Consumer{i}",
            TenantId = "tenant-a",
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Status = InboxMessageStatus.Failed,
            RetryCount = 1,
            ReceivedAtUtc = Now,
        }).ToList();

        await using var context = PlainContext(db);
        context.InboxMessages.AddRange(rows);
        await context.SaveChangesAsync();
        return rows.ConvertAll(r => new InboxMessageKey(r.MessageId, r.ConsumerType));
    }

    private static async Task<List<InboxMessage>> ReadInboxAsync(StoreDatabase db)
    {
        await using var context = PlainContext(db);
        return await context.InboxMessages.AsNoTracking().ToListAsync();
    }

    private static async Task<OutboxBatchResult> ProcessOnceAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredKeyedService<IOutboxProcessor>(EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>())
            .ProcessBatchAsync(10);
    }

    private static IHost BuildHost(StoreDatabase db, CountingTransport transport, CapturingLoggerProvider logs, TimeProvider clock)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSingleton(clock);
        builder.Logging.SetMinimumLevel(LogLevel.Debug).AddProvider(logs);
        builder.Services.AddDbContext<TestMessagingDbContext>(o => o
            .UseNpgsql(db.ConnectionString, npgsql => npgsql.EnableRetryOnFailure()));
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.settle";
            bus.Publishes<SettleEvent>();
            bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
            bus.Services.AddSingleton<IMessageTransport>(transport);
            bus.AddTransportDispatcher();
            bus.Configure(o =>
            {
                o.Outbox.LockDuration = Lease;
                o.Outbox.MaxRetries = 1;
                o.Outbox.RetentionDays = 0;
                o.Inbox.RetentionDays = 0;
            });
        });
        return builder.Build();
    }

    private static async Task<List<OutboxMessage>> ReadOutboxAsync(StoreDatabase db)
    {
        await using var context = PlainContext(db);
        return await context.OutboxMessages.AsNoTracking().ToListAsync();
    }

    /// <summary>Counts each message's sends, and fails those listed in <see cref="Failing"/>.</summary>
    private sealed class CountingTransport : IMessageTransport
    {
        private readonly ConcurrentDictionary<Guid, int> _sends = new();

        public ConcurrentDictionary<Guid, int> Failing { get; } = new();

        public int Sends(Guid id) => _sends.GetValueOrDefault(id);

        public ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
        {
            var message = System.Text.Json.JsonSerializer.Deserialize<SettleEvent>(
                envelope.Body.Span, System.Text.Json.JsonSerializerOptions.Web)!;
            _sends.AddOrUpdate(message.Id, 1, (_, n) => n + 1);
            return Failing.ContainsKey(message.Id)
                ? throw new InvalidOperationException("the transport refuses this message")
                : ValueTask.CompletedTask;
        }
    }
}

[MessageContract("tests.settle.event")]
public sealed record SettleEvent(Guid Id) : IIntegrationEvent;
