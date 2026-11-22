using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// One tenant's unavailable dependency no longer stalls a store's inbox (#124): its retries wait out
/// the tenant's back-off while the other tenants' are handled (D5), its new deliveries are written to
/// the store instead of pausing the transport (D6), and the probe still tells a real outage, which
/// abandons the batch or pauses the transport as before.
/// </summary>
/// <remarks>
/// <see cref="GuardHost"/> over PostgreSQL on a <see cref="FakeTimeProvider"/>: the inbox's polling
/// interval (50 ms there) is the tenant back-off's base. Each test drives the retry batches itself.
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class TenantBackoffInboxTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Polling = TimeSpan.FromMilliseconds(50);

    // ---------- D5: retries ----------

    [DockerRequiredFact]
    public async Task V5_ATenantsDependencyDownOnARetry_DefersThatTenant_AndTheOtherTenantsRowsAreProcessed()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Down.Add("A");
        var a1 = await rig.SeedFailedAsync("A");
        await rig.SeedFailedAsync("B");
        await rig.SeedFailedAsync("B");

        var result = await rig.Host.RetryOnceAsync();

        result.AbortReason.ShouldBe(InboxBatchAbortReason.None, "the batch goes on past A");
        var rows = await rig.Host.InboxAsync();
        rows.Where(r => r.TenantId == "B").ShouldAllBe(r => r.Status == InboxMessageStatus.Processed);
        var a = rows.Single(r => r.RowId == a1.RowId);
        a.Status.ShouldBe(InboxMessageStatus.Failed);
        a.RetryCount.ShouldBe(1, "no attempt is counted for a dependency outage");
        a.DeadLettered.ShouldBeFalse();
        a.NextRetryAtUtc.ShouldBe(Now + Polling, "due at the end of A's back-off");
    }

    [DockerRequiredFact]
    public async Task V5_ADependencyDownForEveryTenant_StillAbandonsTheBatch_AtMostTwoAttemptsPerBatch()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.EveryTenantDown = true;
        string[] tenants = ["A", "B", "C", "D", "E", "F"];
        foreach (var tenant in tenants)
        {
            await rig.SeedFailedAsync(tenant);
        }

        int? allKnownAt = null;
        for (var batch = 0; ; batch++)
        {
            batch.ShouldBeLessThan(40);
            var result = await rig.Host.RetryOnceAsync();

            var calls = rig.TakeCalls();
            calls.Count.ShouldBeLessThanOrEqualTo(2, $"batch {batch}: never one attempt per tenant ({string.Join(", ", calls)})");
            if (calls.Count == 2)
            {
                result.AbortReason.ShouldBe(InboxBatchAbortReason.DependencyUnavailable, $"batch {batch}: abandoned as before #124");
            }

            (await rig.Host.InboxAsync()).ShouldAllBe(r =>
                r.Status == InboxMessageStatus.Failed && r.RetryCount == 1 && !r.DeadLettered);

            if (allKnownAt is not null && batch >= allKnownAt + 2)
            {
                break;
            }

            allKnownAt ??= tenants.All(t => rig.Tenants.TryGetDelay(t, out _)) ? batch : null;
            rig.Clock.Advance(Polling);     // the loop's store-wide back-off starts at the polling interval
        }

        rig.EveryTenantDown = false;
        for (var batch = 0; (await rig.Host.InboxAsync()).Any(r => r.Status != InboxMessageStatus.Processed); batch++)
        {
            batch.ShouldBeLessThan(40, "every row is handled once the dependency is back");
            rig.Clock.Advance(Polling);
            await rig.Host.RetryOnceAsync();
        }
    }

    [DockerRequiredFact]
    public async Task V5_TwoTenantsThatNeverRecover_OldestInTheQueue_CannotHoldAThirdsRetries()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Down.UnionWith(["A", "B"]);
        await rig.SeedFailedAsync("A");
        await rig.SeedFailedAsync("B");
        var c = await rig.SeedFailedAsync("C");

        (await rig.Host.RetryOnceAsync()).AbortReason.ShouldBe(InboxBatchAbortReason.DependencyUnavailable, "A, then the probe B");
        rig.TakeCalls().ShouldBe(["A", "B"]);

        rig.Clock.Advance(Polling);
        await rig.Host.RetryOnceAsync();

        rig.TakeCalls()[0].ShouldBe("C", "the failed probe's tenant has an entry: C goes first (A1)");
        (await rig.Host.InboxAsync()).Single(r => r.RowId == c.RowId).Status.ShouldBe(InboxMessageStatus.Processed);

        for (var batch = 0; batch < 6; batch++)
        {
            rig.Clock.Advance(TimeSpan.FromMinutes(5));
            (await rig.Host.RetryOnceAsync()).AbortReason.ShouldBe(InboxBatchAbortReason.None, "A and B hold only themselves");
        }

        (await rig.Host.InboxAsync()).Where(r => r.TenantId != "C").ShouldAllBe(r =>
            r.Status == InboxMessageStatus.Failed && r.RetryCount == 1 && !r.DeadLettered);
    }

    // ---------- D6: new deliveries ----------

    [DockerRequiredFact]
    public async Task V6_ANewDeliveryWhoseTenantsDependencyIsDown_IsAcknowledgedOnceItsRowIsWritten_AndRetriedOnceTheTenantRecovers()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Down.Add("A");

        var a1 = await rig.ReceiveAsync("A");
        a1.Result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge, "the store's retry loop owns A's row now");
        var row = (await rig.Host.InboxAsync()).Single(r => r.MessageId.Value == a1.Id);
        row.Status.ShouldBe(InboxMessageStatus.Failed);
        row.RetryCount.ShouldBe(0, "no attempt is counted for a dependency outage");
        row.DeadLettered.ShouldBeFalse();
        row.NextRetryAtUtc.ShouldBe(Now + Polling);

        // B is not paused behind A.
        var b1 = await rig.ReceiveAsync("B");
        b1.Result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        (await rig.Host.InboxAsync()).Single(r => r.MessageId.Value == b1.Id).Status.ShouldBe(InboxMessageStatus.Processed);

        // A is in back-off: its next delivery is written, unattempted.
        rig.TakeCalls().ShouldBe(["A", "B"]);
        var a2 = await rig.ReceiveAsync("A");
        a2.Result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        rig.TakeCalls().ShouldBeEmpty("a delivery of a tenant in back-off does not run its handler");
        var deferred = (await rig.Host.InboxAsync()).Single(r => r.MessageId.Value == a2.Id);
        deferred.Status.ShouldBe(InboxMessageStatus.Failed);
        deferred.RetryCount.ShouldBe(0);
        deferred.NextRetryAtUtc.ShouldBe(Now + Polling, "due at the end of A's back-off");
        rig.Host.Logs.Entries.ShouldContain(e => e.EventId == 2014, "each deferred delivery logs at Debug");
        rig.Host.Logs.Entries.Count(e => e.EventId == 2013).ShouldBe(1, "2013 fires when A enters back-off, not per delivery");

        // A recovers: the store's loop retries both rows.
        rig.Down.Clear();
        rig.Clock.Advance(Polling);
        await rig.Host.RetryOnceAsync();
        (await rig.Host.InboxAsync()).ShouldAllBe(r => r.Status == InboxMessageStatus.Processed);
    }

    [DockerRequiredFact]
    public async Task V6_ADeferredRowThatCannotBeWritten_LeavesTheDeliveryPaused()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Down.Add("A");
        rig.Host.FailedRowInserts.Armed = true;

        var a1 = await rig.ReceiveAsync("A");

        a1.Result.Disposition.ShouldBe(ReceiveDisposition.PauseAndRedeliver, "acknowledged only once the row has committed");
        (await rig.Host.InboxAsync()).ShouldBeEmpty();
        rig.Host.Logs.Entries.ShouldNotContain(e => e.EventId == 2013, "2013 reports a row that waits, once it is written");
    }

    [DockerRequiredFact]
    public async Task V6_TwoTenantsDown_PauseTheTransportOnce_ThenHoldOnlyThemselves()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Down.UnionWith(["A", "B"]);

        (await rig.ReceiveAsync("A")).Result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        var b = await rig.ReceiveAsync("B");
        b.Result.Disposition.ShouldBe(ReceiveDisposition.PauseAndRedeliver, "the probe failed: a shared outage, the broker keeps it");
        (await rig.Host.InboxAsync()).ShouldNotContain(r => r.TenantId == "B");

        (await rig.ReceiveAsync("C")).Result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        rig.TakeCalls().ShouldBe(["A", "B", "C"]);

        // B's redelivery: the failed probe's tenant is in back-off (A1), so it is written, not run.
        (await rig.ReceiveAsync("B", b.Id)).Result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        rig.TakeCalls().ShouldBeEmpty();
        var row = (await rig.Host.InboxAsync()).Single(r => r.TenantId == "B");
        row.Status.ShouldBe(InboxMessageStatus.Failed);
        row.RetryCount.ShouldBe(0);
    }

    [DockerRequiredFact]
    public async Task V6_OverTheInMemoryTransport_ATenantsDependencyDown_DoesNotPauseTheOtherTenants()
    {
        // A pause would hold B for an hour: B handled within the test's deadline proves none happened.
        var broker = new InMemoryBroker { DependencyUnavailableBackoff = TimeSpan.FromHours(1) };
        await using var rig = await Rig.CreateAsync(fixture, broker);
        rig.Down.Add("A");
        await rig.Host.StartAsync();

        await rig.PublishAsync("A", Guid.NewGuid());
        await rig.PublishAsync("B", Guid.NewGuid());

        // The bus mints each publication's message id: the rows are found by tenant.
        var deadline = Stopwatch.StartNew();
        while ((await rig.Host.InboxAsync()).SingleOrDefault(r => r.TenantId == "B")?.Status != InboxMessageStatus.Processed)
        {
            deadline.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20), "B is delivered behind A, never paused");
            await Task.Delay(20);
        }

        var rowOfA = (await rig.Host.InboxAsync()).Single(r => r.TenantId == "A");
        rowOfA.Status.ShouldBe(InboxMessageStatus.Failed);
        rowOfA.RetryCount.ShouldBe(0);
        rowOfA.NextRetryAtUtc.ShouldNotBeNull();
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<Guid, string> _tenantOf = new();
        private readonly ConcurrentQueue<string> _calls = new();
        private int _order;

        private Rig(GuardHost host, FakeTimeProvider clock)
        {
            Host = host;
            Clock = clock;
            Host.Probe.Scripts[FirstGuardHandler.HandlerName] = call =>
            {
                var tenant = _tenantOf[call.Message.Id];
                _calls.Enqueue(tenant);
                return EveryTenantDown || Down.Contains(tenant)
                    ? throw new InboxDependencyUnavailableException($"Tenant '{tenant}''s partner API is down.")
                    : Task.CompletedTask;
            };
        }

        public GuardHost Host { get; }

        public FakeTimeProvider Clock { get; }

        public ConcurrentHashSet Down { get; } = new();

        public bool EveryTenantDown { get; set; }

        public TenantBackoff Tenants => Host.Provider.GetRequiredService<InboxTenantBackoff>().For(GuardHost.Store);

        public static async Task<Rig> CreateAsync(PostgreSqlFixture fixture, InMemoryBroker? broker = null)
        {
            var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
            var clock = new FakeTimeProvider(Now);
            var host = GuardHost.Create(db, bus => bus.AddHandler<FirstGuardHandler>(), broker: broker, clock: clock);
            return new Rig(host, clock);
        }

        public List<string> TakeCalls()
        {
            var taken = new List<string>();
            while (_calls.TryDequeue(out var tenant))
            {
                taken.Add(tenant);
            }

            return taken;
        }

        public async Task<InboxMessage> SeedFailedAsync(string tenant)
        {
            var id = Guid.NewGuid();
            _tenantOf[id] = tenant;
            var row = new InboxMessage
            {
                MessageId = MessageId.From(id),
                ConsumerType = MessageConsumerKey.Of(typeof(FirstGuardHandler)),
                TenantId = tenant,
                EventType = typeof(GuardEvent).AssemblyQualifiedName!,
                Payload = JsonSerializer.Serialize(new GuardEvent(id), JsonSerializerOptions.Web),
                Source = GuardHost.ServiceName,
                Status = InboxMessageStatus.Failed,
                RetryCount = 1,
                ReceivedAtUtc = Now.AddSeconds(++_order - 10_000),
                OccurredOnUtc = Now,
            };

            await using var context = Host.NewContext();
            context.InboxMessages.Add(row);
            await context.SaveChangesAsync();
            return row;
        }

        public async Task<(Guid Id, EnvelopeReceiveResult Result)> ReceiveAsync(string tenant, Guid? id = null)
        {
            var messageId = id ?? Guid.NewGuid();
            _tenantOf[messageId] = tenant;
            var envelope = new MessageEnvelope
            {
                Id = messageId.ToString(),
                Source = GuardHost.ServiceName,
                Type = GuardEvent.WireType,
                Time = Now,
                DataContentType = "application/json",
                Headers = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [MessageHeaders.CorrelationId] = Guid.NewGuid().ToString(),
                    [MessageHeaders.TenantId] = tenant,
                },
                Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new GuardEvent(messageId), JsonSerializerOptions.Web)),
            };

            return (messageId, await Host.ReceiveAsync(envelope));
        }

        public async Task PublishAsync(string tenant, Guid id)
        {
            _tenantOf[id] = tenant;
            await using var scope = Host.Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                .PublishAsync(new GuardEvent(id), new PublishOptions { TenantId = tenant });
            await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await Host.Database.DisposeAsync();
        }
    }

    /// <summary>A set the handler reads from transport threads while the test writes it.</summary>
    private sealed class ConcurrentHashSet
    {
        private readonly ConcurrentDictionary<string, byte> _items = new(StringComparer.Ordinal);

        public void Add(string item) => _items[item] = 0;

        public void UnionWith(IEnumerable<string> items)
        {
            foreach (var item in items)
            {
                Add(item);
            }
        }

        public bool Contains(string item) => _items.ContainsKey(item);

        public void Clear() => _items.Clear();
    }
}
