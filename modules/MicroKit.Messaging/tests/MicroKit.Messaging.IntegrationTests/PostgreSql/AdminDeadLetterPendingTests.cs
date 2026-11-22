using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The operator's way out for a tenant that will not recover (#124 D7): one statement dead-letters that
/// tenant's waiting rows and nothing else, and the existing dead-letter tooling brings them back.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class AdminDeadLetterPendingTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [DockerRequiredFact]
    public async Task Outbox_DeadLetterPendingAsync_ParksOnlyThatTenantsUnclaimedPendingRows_AndRequeueBringsOneBack()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var clock = new FakeTimeProvider(Now);
        var transport = new RecordingTransport();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IMessageTransport>(transport);
        services.AddDbContext<TestMessagingDbContext>(o => o.UseNpgsql(db.ConnectionString));
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.admin";
            bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
        });
        await using var provider = services.BuildServiceProvider();
        var store = EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>();

        var pendingA1 = Outbox("A");
        var pendingA2 = Outbox("A", retryCount: 2);
        var claimedA = Outbox("A", OutboxMessageStatus.Processing, claimToken: Guid.NewGuid());
        var deadA = Outbox("A", OutboxMessageStatus.Failed, deadLettered: true, error: "earlier");
        var publishedA = Outbox("A", OutboxMessageStatus.Published);
        var pendingB = Outbox("B");
        var pendingNull = Outbox(null);
        OutboxMessage[] all = [pendingA1, pendingA2, claimedA, deadA, publishedA, pendingB, pendingNull];
        await using (var seed = NewContext(db))
        {
            seed.OutboxMessages.AddRange(all);
            await seed.SaveChangesAsync();
        }

        var reason = "Parked by an operator: tenant A is de-provisioned. " + new string('x', 3000);
        int parked;
        await using (var scope = provider.CreateAsyncScope())
        {
            parked = await scope.ServiceProvider.GetRequiredKeyedService<IOutboxAdminStore>(store).DeadLetterPendingAsync("A", reason);
        }

        parked.ShouldBe(2, "A's two unclaimed pending rows");
        var rows = (await ReadOutboxAsync(db)).ToDictionary(r => r.Id);
        foreach (var id in new[] { pendingA1.Id, pendingA2.Id })
        {
            rows[id].Status.ShouldBe(OutboxMessageStatus.Failed);
            rows[id].DeadLettered.ShouldBeTrue();
            rows[id].ProcessedAtUtc.ShouldBe(Now);
            rows[id].ErrorMessage.ShouldNotBeNull().Length.ShouldBe(2048, "cut to the mapped column");
            rows[id].ErrorMessage!.ShouldStartWith("Parked by an operator");
        }

        rows[pendingA2.Id].RetryCount.ShouldBe(2, "its retry count is left alone");
        foreach (var untouched in new[] { claimedA, deadA, publishedA, pendingB, pendingNull })
        {
            rows[untouched.Id].Status.ShouldBe(untouched.Status);
            rows[untouched.Id].DeadLettered.ShouldBe(untouched.DeadLettered);
            rows[untouched.Id].ErrorMessage.ShouldBe(untouched.ErrorMessage);
        }

        // The way back: the existing dead-letter tooling.
        await using (var scope = provider.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredKeyedService<IOutboxAdminStore>(store);
            var listed = await admin.GetDeadLetteredAsync(10, "A");
            listed.Select(m => m.Id).ShouldBe([pendingA1.Id, pendingA2.Id, deadA.Id], ignoreOrder: true);
            (await admin.RequeueAsync(pendingA1.Id)).ShouldBeTrue();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredKeyedService<IOutboxProcessor>(store).ProcessBatchAsync(10);
        }

        (await ReadOutboxAsync(db)).Single(r => r.Id == pendingA1.Id).Status.ShouldBe(OutboxMessageStatus.Published,
            "A healthy again, the requeued row is dispatched");
        transport.Tenants.ShouldContain("A");
    }

    [DockerRequiredFact]
    public async Task Inbox_DeadLetterPendingAsync_ParksOnlyThatTenantsUnclaimedWaitingRows_AndRequeueBringsOneBack()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var clock = new FakeTimeProvider(Now);
        await using var host = GuardHost.Create(db, bus => bus.AddHandler<FirstGuardHandler>(), clock: clock);

        var failedA = Inbox("A", InboxMessageStatus.Failed, retryCount: 3);
        var receivedA = Inbox("A", InboxMessageStatus.Received);
        var claimedA = Inbox("A", InboxMessageStatus.Processing, claimToken: Guid.NewGuid());
        var processedA = Inbox("A", InboxMessageStatus.Processed);
        var deadA = Inbox("A", InboxMessageStatus.Failed, deadLettered: true, error: "earlier");
        var failedB = Inbox("B", InboxMessageStatus.Failed);
        InboxMessage[] all = [failedA, receivedA, claimedA, processedA, deadA, failedB];
        await using (var seed = host.NewContext())
        {
            seed.InboxMessages.AddRange(all);
            await seed.SaveChangesAsync();
        }

        int parked;
        await using (var scope = host.Provider.CreateAsyncScope())
        {
            parked = await scope.ServiceProvider.GetRequiredKeyedService<IInboxAdminStore>(GuardHost.Store)
                .DeadLetterPendingAsync("A", "Parked by an operator");
        }

        parked.ShouldBe(2, "A's unclaimed Failed and Received rows");
        var rows = (await host.InboxAsync()).ToDictionary(r => r.RowId);
        foreach (var id in new[] { failedA.RowId, receivedA.RowId })
        {
            rows[id].Status.ShouldBe(InboxMessageStatus.Failed);
            rows[id].DeadLettered.ShouldBeTrue();
            rows[id].ProcessedAtUtc.ShouldBe(Now);
            rows[id].ErrorMessage.ShouldBe("Parked by an operator");
        }

        rows[failedA.RowId].RetryCount.ShouldBe(3, "its retry count is left alone");
        foreach (var untouched in new[] { claimedA, processedA, deadA, failedB })
        {
            rows[untouched.RowId].Status.ShouldBe(untouched.Status);
            rows[untouched.RowId].DeadLettered.ShouldBe(untouched.DeadLettered);
            rows[untouched.RowId].ErrorMessage.ShouldBe(untouched.ErrorMessage);
        }

        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredKeyedService<IInboxAdminStore>(GuardHost.Store);
            var listed = await admin.GetDeadLetteredAsync(10, "A");
            listed.Select(m => m.RowId).ShouldBe([failedA.RowId, receivedA.RowId, deadA.RowId], ignoreOrder: true);
            (await admin.RequeueAsync(new InboxMessageKey(failedA.MessageId, failedA.ConsumerType))).ShouldBeTrue();
        }

        await host.RetryOnceAsync();

        (await host.InboxAsync()).Single(r => r.RowId == failedA.RowId).Status.ShouldBe(InboxMessageStatus.Processed,
            "A healthy again, the requeued row is handled");
    }

    private static int s_order;

    private static OutboxMessage Outbox(
        string? tenant,
        OutboxMessageStatus status = OutboxMessageStatus.Pending,
        int retryCount = 0,
        bool deadLettered = false,
        Guid? claimToken = null,
        string? error = null) => new()
    {
        Id = MessageId.New(),
        ContractName = "tests.admin.event.v1",
        Source = "tests.admin",
        TenantId = tenant,
        EventType = typeof(GuardEvent).AssemblyQualifiedName!,
        Payload = "{}",
        Status = status,
        RetryCount = retryCount,
        DeadLettered = deadLettered,
        ClaimToken = claimToken,
        LockedUntilUtc = claimToken is null ? null : Now.AddMinutes(5),
        ErrorMessage = error,
        ProcessedAtUtc = status is OutboxMessageStatus.Published || deadLettered ? Now.AddDays(-1) : null,
        OccurredOnUtc = Now,
        CreatedAtUtc = Now.AddSeconds(Interlocked.Increment(ref s_order) - 10_000),
        CorrelationId = CorrelationId.New(),
    };

    private static InboxMessage Inbox(
        string tenant,
        InboxMessageStatus status,
        int retryCount = 1,
        bool deadLettered = false,
        Guid? claimToken = null,
        string? error = null)
    {
        var id = Guid.NewGuid();
        return new InboxMessage
        {
            MessageId = MessageId.From(id),
            ConsumerType = MessageConsumerKey.Of(typeof(FirstGuardHandler)),
            TenantId = tenant,
            EventType = typeof(GuardEvent).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(new GuardEvent(id), JsonSerializerOptions.Web),
            Source = GuardHost.ServiceName,
            Status = status,
            RetryCount = retryCount,
            DeadLettered = deadLettered,
            ClaimToken = claimToken,
            LockedUntilUtc = claimToken is null ? null : Now.AddMinutes(5),
            ErrorMessage = error,
            ProcessedAtUtc = status is InboxMessageStatus.Processed || deadLettered ? Now.AddDays(-1) : null,
            ReceivedAtUtc = Now.AddSeconds(Interlocked.Increment(ref s_order) - 10_000),
            OccurredOnUtc = Now,
        };
    }

    private static TestMessagingDbContext NewContext(StoreDatabase db)
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(db.ConnectionString).Options);

    private static async Task<List<OutboxMessage>> ReadOutboxAsync(StoreDatabase db)
    {
        await using var context = NewContext(db);
        return await context.OutboxMessages.AsNoTracking().ToListAsync();
    }

    private sealed class RecordingTransport : IMessageTransport
    {
        public List<string?> Tenants { get; } = [];

        public ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
        {
            Tenants.Add(envelope.Headers.GetValueOrDefault(MessageHeaders.TenantId));
            return ValueTask.CompletedTask;
        }
    }
}
