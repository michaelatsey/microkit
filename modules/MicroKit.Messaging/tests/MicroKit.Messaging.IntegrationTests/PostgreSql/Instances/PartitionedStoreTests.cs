using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MicroKit.Messaging.IntegrationTests.SecondModule;
using Xunit.Abstractions;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql.Instances;

/// <summary>
/// A partitioned store (#191) on one PostgreSQL server holding a database per instance: every scope is
/// prepared for its instance, instances come and go at runtime, a removal settles what is in flight, and
/// a delivery's back-off and its instance's retry loop share one state.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class PartitionedStoreTests(InstanceServerFixture server, ITestOutputHelper output)
    : IClassFixture<InstanceServerFixture>
{
    private static readonly MessageStoreKey Store = PartitionedHost.Store;

    /// <summary>
    /// V1: fifty instances. Every transport row, local row and due retry is processed in its own database:
    /// read and settled where it was written, sent from a scope prepared for its instance, and each local
    /// delivery's inbox row written next to its outbox row. The connection budget holds (A5).
    /// </summary>
    [DockerRequiredFact]
    public async Task FiftyInstances_EveryRowAndDueRetry_IsProcessedInItsOwnDatabase()
    {
        await using var host = PartitionedHost.Build();
        host.Output = output;
        var ids = await host.ProvisionAsync(server, InstanceServerFixture.MaxDatabases);

        var published = new List<(string Instance, Guid Transport, Guid Local, Guid Retry)>();
        foreach (var id in ids)
        {
            var (transport, local) = await host.PublishAsync(id);
            published.Add((id, transport, local, await host.SeedDueRetryAsync(id)));
        }

        await using var sampler = new ConnectionSampler(server);
        host.Declare(ids);
        await host.StartAsync();

        await Eventually.HoldsAsync(
            () => host.Recorder.Sent.Count >= ids.Length && host.Recorder.Handled.Count >= 2 * ids.Length,
            "every instance's transport row is sent, and its local row and its retry are handled");

        var sent = host.Recorder.Sent.ToDictionary(s => s.MessageId, s => s.Database);
        var handled = host.Recorder.Handled.ToDictionary(h => h.MessageId, h => h.Database);
        foreach (var (instance, transport, local, retry) in published)
        {
            var database = host.Catalog.DatabaseOfInstance(instance);
            sent[transport].ShouldBe(database, "the transport row was dispatched from a scope prepared for its instance");
            handled[local].ShouldBe(database, "the local consumer ran against the row's own instance");
            handled[retry].ShouldBe(database, "the retry ran in the database that holds its row");

            await Eventually.HoldsAsync(
                async () => (await host.OutboxOfAsync(instance)).All(r => r.Status == OutboxMessageStatus.Published),
                $"instance {instance}'s outbox rows are settled where they were written");
            var inbox = await host.InboxOfAsync(instance);
            inbox.ShouldContain(
                r => r.MessageId.Value == local && r.ConsumerType == InstanceNoticeConsumers.Consumer && r.Status == InboxMessageStatus.Processed,
                "a local delivery's inbox row is written in the database of its outbox row");
            inbox.Single(r => r.MessageId.Value == retry).Status.ShouldBe(InboxMessageStatus.Processed);
            inbox.Count.ShouldBe(2, "nothing of another instance landed here");
        }

        // A7: a processor's logs inside an instance's loop carry the instance in their logging scope.
        host.Logs.Entries.ShouldContain(e =>
            e.Category.EndsWith("OutboxProcessor", StringComparison.Ordinal)
            && Equals(e.Scope.GetValueOrDefault("Store"), Store.Value)
            && e.Scope.GetValueOrDefault("Instance") is string);

        var peak = sampler.Peak;
        output.WriteLine($"V1 peak client connections: {peak} (budget: {InstanceServerFixture.MaxDatabases} databases x " +
                         $"{InstanceServerFixture.PoolSizePerDatabase} pooled, server max_connections {InstanceServerFixture.MaxConnections})");
        peak.ShouldBeLessThan(InstanceServerFixture.MaxConnections);
    }

    /// <summary>
    /// V2: an instance added at runtime is drained without a restart; a removed one is no longer claimed —
    /// claims counted per database — and its rows are untouched; re-added, it is drained again.
    /// </summary>
    [DockerRequiredFact]
    public async Task AnInstanceAddedAtRuntime_IsDrained_ARemovedOneIsNoLongerClaimed_AndReAddedItIsDrainedAgain()
    {
        await using var host = PartitionedHost.Build();
        host.Output = output;
        await host.ProvisionAsync(server, 3);
        host.Declare("i0", "i1");
        await host.StartAsync();
        var (a, b, c) = (host.Catalog.DatabaseOfInstance("i0"), host.Catalog.DatabaseOfInstance("i1"), host.Catalog.DatabaseOfInstance("i2"));

        await SentAsync(host, await host.PublishTransportAsync("i0"), "control: a declared instance is drained");

        // Added at runtime.
        var early = await host.PublishTransportAsync("i2");
        host.Declare("i0", "i1", "i2");
        await SentAsync(host, early, "an instance added at runtime is drained without a restart");

        // Removed: once its loops have ended, it is claimed no more, and its rows stay as written.
        host.Declare("i0", "i2");
        await ReleasedAsync(host, "i1");
        var claimsOfB = host.Recorder.ClaimsOf(b);
        var waiting = await host.PublishTransportAsync("i1");
        var claimsOfA = host.Recorder.ClaimsOf(a);
        await SentAsync(host, await host.PublishTransportAsync("i0"), "control: the engine keeps cycling");
        await Eventually.HoldsAsync(() => host.Recorder.ClaimsOf(a) >= claimsOfA + 5, "several more cycles of another instance");

        host.Recorder.ClaimsOf(b).ShouldBe(claimsOfB, "a removed instance is no longer claimed");
        var row = (await host.OutboxOfAsync("i1")).Single(r => r.Id.Value == waiting);
        row.Status.ShouldBe(OutboxMessageStatus.Pending, "its rows stay where they are");
        row.RetryCount.ShouldBe(0);
        row.ClaimToken.ShouldBeNull();
        host.Recorder.ClaimsOf(c).ShouldBeGreaterThan(0);

        // Re-added: drained again.
        host.Declare("i0", "i1", "i2");
        await SentAsync(host, waiting, "re-added, it is drained");
    }

    /// <summary>
    /// V3: removed while a batch is in flight, the batch settles — Published, none Released — and the
    /// instance claims nothing after.
    /// </summary>
    [DockerRequiredFact]
    public async Task RemovedWhileABatchIsInFlight_TheBatchSettles_ThenNothingIsClaimed()
    {
        await using var host = PartitionedHost.Build();
        host.Output = output;
        await host.ProvisionAsync(server, 2);
        var a = host.Catalog.DatabaseOfInstance("i0");
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Recorder.HeldSends[a] = held;
        var rows = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            rows.Add(await host.PublishTransportAsync("i0"));
        }

        host.Declare("i0", "i1");
        await host.StartAsync();
        await Eventually.HoldsAsync(
            async () => (await host.OutboxOfAsync("i0")).All(r => r.Status == OutboxMessageStatus.Processing),
            "control: the batch is claimed and its first send is in flight");

        host.Declare("i1");
        var registry = host.Services.GetRequiredService<StoreInstanceRegistry>();
        await Eventually.HoldsAsync(() => registry.Find(new StoreInstanceKey(Store, "i0")) is null, "the instance is removed");
        held.SetResult();
        await ReleasedAsync(host, "i0");

        var settled = await host.OutboxOfAsync("i0");
        settled.ShouldAllBe(r => r.Status == OutboxMessageStatus.Published, "the batch in flight settled: none was released");
        settled.ShouldAllBe(r => r.RetryCount == 0);
        host.Recorder.Sent.Count(s => s.Database == a).ShouldBe(rows.Count);

        var claims = host.Recorder.ClaimsOf(a);
        var after = await host.PublishTransportAsync("i0");
        var claimsOfOther = host.Recorder.ClaimsOf(host.Catalog.DatabaseOfInstance("i1"));
        await Eventually.HoldsAsync(
            () => host.Recorder.ClaimsOf(host.Catalog.DatabaseOfInstance("i1")) >= claimsOfOther + 5,
            "several more cycles of the other instance");
        host.Recorder.ClaimsOf(a).ShouldBe(claims, "then no claim");
        (await host.OutboxOfAsync("i0")).Single(r => r.Id.Value == after).Status.ShouldBe(OutboxMessageStatus.Pending);
    }

    /// <summary>
    /// V6 (D4): a delivery whose dependency is down for tenant T on instance I blocks T for I's retry loop —
    /// I's claims leave T out — and not for instance J.
    /// </summary>
    [DockerRequiredFact]
    public async Task ADeliveryWhoseDependencyIsDownForATenantOnOneInstance_BlocksItForThatInstancesRetriesOnly()
    {
        // A clock that does not move: a tenant put in back-off stays there for the whole test, so "I's
        // claims leave T out" does not race the curve's first delay. The loops' cadence is real time.
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = PartitionedHost.Build(services: s =>
        {
            s.AddSingleton<TimeProvider>(clock);
            s.AddSingleton(new InstanceMapping
            {
                Map = static context => context.Headers.TryGetValue("x-instance", out var id) ? id : null,
            });
        });
        host.Output = output;
        await host.ProvisionAsync(server, 2);
        var (i, j) = (host.Catalog.DatabaseOfInstance("i0"), host.Catalog.DatabaseOfInstance("i1"));
        host.Declare("i0", "i1");
        await host.StartAsync();
        host.Recorder.DependencyDown = (i, "T");

        var receiver = host.Services.GetRequiredService<IEnvelopeReceiver>();
        var delivered = Guid.NewGuid();
        var result = await receiver.ReceiveAsync(PartitionedHost.Envelope(delivered, "T", new Dictionary<string, string> { ["x-instance"] = "i0" }));
        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge, "the tenant's row is written to wait out its back-off; the delivery is done");
        var mark = host.Recorder.InboxClaims.Count;

        var retryOfI = await host.SeedDueRetryAsync("i0", tenant: "T");
        var retryOfJ = await host.SeedDueRetryAsync("i1", tenant: "T");

        await Eventually.HoldsAsync(() => host.Recorder.Handled.Any(h => h.MessageId == retryOfJ), "instance J retries tenant T");
        await Eventually.HoldsAsync(
            () => host.Recorder.InboxClaims.Skip(mark).Count(c => c.Database == i) >= 3,
            "instance I's retry loop claimed several times since the delivery");

        var claimsOfI = host.Recorder.InboxClaims.Skip(mark).Where(c => c.Database == i).ToList();
        claimsOfI.ShouldAllBe(c => c.ExcludedTenants.Contains("T"), "I's claims leave T out: the delivery's back-off is I's own");
        host.Recorder.InboxClaims.Skip(mark).Where(c => c.Database == j).ShouldAllBe(c => c.ExcludedTenants.Length == 0, "J's are not");
        (await host.InboxOfAsync("i0")).Single(r => r.MessageId.Value == retryOfI).RetryCount.ShouldBe(1, "T's row in I was not attempted");
        host.Recorder.Handled.ShouldNotContain(h => h.MessageId == delivered);
    }

    /// <summary>
    /// V7 (A2): a delivery in flight across a removal writes into the released state; no inbox back-off
    /// entry and no series of the instance is left.
    /// </summary>
    [DockerRequiredFact]
    public async Task ADeliveryInFlightAcrossARemoval_LeavesNoBackOffEntry_AndNoSeries()
    {
        await using var host = PartitionedHost.Build(options: o => o.Instances.TagInstances = true);
        host.Output = output;
        await host.ProvisionAsync(server, 2);
        var k = host.Catalog.DatabaseOfInstance("i0");
        host.Declare("i0", "i1");
        await host.StartAsync();

        var inHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Recorder.HoldHandler = (inHandler, resume);
        host.Recorder.DependencyDown = (k, "i0");

        var receiver = host.Services.GetRequiredService<IEnvelopeReceiver>();
        var delivery = receiver.ReceiveAsync(PartitionedHost.Envelope(Guid.NewGuid(), "i0")).AsTask();
        await inHandler.Task.WaitAsync(TimeSpan.FromSeconds(30));

        host.Declare("i1");
        await ReleasedAsync(host, "i0");
        resume.SetResult();
        await delivery.WaitAsync(TimeSpan.FromSeconds(30));

        var key = new StoreInstanceKey(Store, "i0");
        host.Services.GetRequiredService<InboxTenantBackoff>().Stores.ShouldNotContain(e => e.Key == key, "the delivery path never creates an instance's state");
        using var recorder = new InstanceMeterRecorder(
            host.Services.GetRequiredService<InboxMetrics>().Meter,
            host.Services.GetRequiredService<OutboxMetrics>().Meter);
        recorder.Observed().ShouldNotContain(m => m.Instance == "i0", "a released instance's series no longer report");
        recorder.Observed().ShouldContain(m => m.Instance == "i1", "control: the drained instance reports");
    }

    /// <summary>
    /// V9 (D7): a partitioned store whose context can only be built in an instance's scope starts the host;
    /// a partitioned store whose model lacks the messaging entities is refused at its first instance
    /// (Critical), and the other store's instances are drained.
    /// </summary>
    [DockerRequiredFact]
    public async Task ACheckOnlyAnInstanceCanRun_StartsTheHost_AndABadModelIsRefusedAtItsFirstInstanceAlone()
    {
        var bare = EntityFrameworkOutbox.KeyOf<BareDbContext>();
        await using var host = PartitionedHost.Build(
            bus: b => b.UseEntityFrameworkOutbox<BareDbContext>(s => s
                .ForAssemblyContaining<SecondModuleMarker>()
                .PartitionedBy<BareInstanceSource, TestInstanceInitializer>()),
            services: s => s.AddDbContext<BareDbContext>((sp, o) => o.UseNpgsql(sp.GetRequiredService<InstanceConnection>().Required)));
        host.Output = output;
        await host.ProvisionAsync(server, 1, prefix: "good");
        await host.ProvisionAsync(server, 1, prefix: "bare");
        host.Declare("good0");
        host.Catalog.Set(bare, "bare0");

        // Neither context can be built outside an instance's scope: a start-time model check would fail.
        await host.StartAsync();

        await Eventually.HoldsAsync(
            () => host.Logs.Entries.Any(e => e.EventId == 1409 && e.Level == LogLevel.Critical && e.Message.Contains(bare.Value, StringComparison.Ordinal)),
            "the bare store is refused, at Critical, when its first instance starts");
        host.Logs.Entries.ShouldNotContain(e => e.EventId == 1409 && e.Message.Contains(Store.Value + "'", StringComparison.Ordinal));
        await SentAsync(host, await host.PublishTransportAsync("good0"), "the other store's instance is drained");
        host.Services.GetRequiredService<StoreInstanceRegistry>().IsRefused(bare).ShouldBeTrue();
    }

    /// <summary>
    /// V13 (A1): a delivery whose tenant maps to no instance drained here is a counted failure the transport
    /// redelivers; it does not pause the endpoint, so a mapped delivery behind it is handled at once.
    /// </summary>
    [DockerRequiredFact]
    public async Task AnUnmappedDelivery_DoesNotDelayAMappedOne_OnTheSameEndpoint()
    {
        var broker = new InMemoryBroker { DependencyUnavailableBackoff = TimeSpan.FromHours(1), MaxDeliveryAttempts = 3 };
        await using var host = PartitionedHost.Build(scopedTransport: false, bus: b => b.UseInMemoryTransport(broker));
        host.Output = output;
        await host.ProvisionAsync(server, 1, prefix: "y");
        host.Declare("y0");
        await host.StartAsync();

        var publisher = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        publisher.Services.AddMicroKitMessaging(b =>
        {
            b.ServiceName = "tests.instances.publisher";
            b.Publishes<InstanceEvent>();
            b.UseInMemoryTransport(broker);
        });
        using var producer = publisher.Build();
        await producer.StartAsync();

        Guid unmapped, mapped = Guid.NewGuid();
        await using (var scope = producer.Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            unmapped = (await bus.PublishAsync(new InstanceEvent(Guid.NewGuid()), new PublishOptions { TenantId = "x0" })).Value;
            await bus.PublishAsync(new InstanceEvent(mapped), new PublishOptions { TenantId = "y0" });
        }

        await Eventually.HoldsAsync(
            () => host.Recorder.Handled.Any(h => h.MessageId == mapped),
            "the mapped delivery is handled: the unmapped one ahead of it paused nothing",
            TimeSpan.FromSeconds(30));
        broker.GetFailedAttempts("tests.instances", unmapped.ToString()).ShouldBeGreaterThan(0, "the unmapped delivery is counted");
        host.Logs.Entries.ShouldContain(e => e.EventId == 1410 && e.Message.Contains(unmapped.ToString(), StringComparison.Ordinal));
        await producer.StopAsync();
    }

    private static Task SentAsync(PartitionedHost host, Guid message, string because)
        => Eventually.HoldsAsync(() => host.Recorder.Sent.Any(s => s.MessageId == message), because);

    /// <summary>Waits until a removed instance's loops have ended and its state was released.</summary>
    private static Task ReleasedAsync(PartitionedHost host, string id)
    {
        var key = new StoreInstanceKey(Store, id);
        var inbox = host.Services.GetRequiredService<InboxTenantBackoff>();
        return Eventually.HoldsAsync(() => !inbox.Stores.Any(e => e.Key == key), $"instance {id}'s loops have ended and its state was released");
    }
}

/// <summary>A context whose model carries no messaging entity (V9).</summary>
public sealed class BareDbContext(DbContextOptions<BareDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<BusinessRecord>().ToTable("BusinessRecords").HasKey(r => r.Id);
}
