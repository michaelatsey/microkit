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
