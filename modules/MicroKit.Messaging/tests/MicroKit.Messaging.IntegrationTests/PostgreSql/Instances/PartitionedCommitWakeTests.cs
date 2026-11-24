using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql.Instances;

/// <summary>
/// V7 (#215 D3): in a partitioned store, a commit wakes the instance its scope serves and no other; a
/// publication from a host scope names no instance, throws nothing and is published by polling (#189).
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class PartitionedCommitWakeTests(InstanceServerFixture server, ITestOutputHelper output)
    : IClassFixture<InstanceServerFixture>
{
    private static readonly MessageStoreKey Store = PartitionedHost.Store;
    private static readonly TimeSpan Polling = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan Bound = CommitWakeTests.Bound;

    /// <summary>max(10 ms, 5% of the wait): the rule of the unit suite's <c>OutboxWakeTests.Tolerance</c>.</summary>
    private static TimeSpan Tolerance(TimeSpan wait)
    {
        var proportional = wait * 0.05;
        var least = TimeSpan.FromMilliseconds(10);
        return proportional > least ? proportional : least;
    }

    [DockerRequiredFact]
    public async Task APublicationInAnInstancesDeliveryScope_WakesThatInstance_AndNotItsSibling()
    {
        await using var host = Build(maxPolling: TimeSpan.FromMinutes(10));
        host.Output = output;
        var ids = await host.ProvisionAsync(server, 2);
        var (i, j) = (ids[0], ids[1]);
        var (dbI, dbJ) = (host.Catalog.DatabaseOfInstance(i), host.Catalog.DatabaseOfInstance(j));
        var claims = host.Services.GetRequiredService<ClaimTimeline>();
        host.Declare(ids);
        await host.StartAsync();
        await Eventually.HoldsAsync(
            () => claims.Completed(dbI) >= 3 && claims.Completed(dbJ) >= 3, "three empty claims each: both wait 8 s");

        // A delivery mapped to I (its tenant names the instance); its handler publishes, and the engine's
        // commit of the attempt ends I's scope.
        var trigger = Guid.NewGuid();
        var result = await host.Services.GetRequiredService<IEnvelopeReceiver>().ReceiveAsync(Envelope(trigger, tenant: i));
        var committed = Stopwatch.GetTimestamp();
        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge, "control: the handler's attempt committed");

        var published = host.Services.GetRequiredService<Republished>().Of[trigger];
        await Eventually.HoldsAsync(() => host.Recorder.Sent.Any(s => s.MessageId == published), "I's publication is sent");
        var sent = host.Recorder.Sent.First(s => s.MessageId == published);
        sent.Database.ShouldBe(dbI, "control: the publication was enlisted in I's database");
        var latency = Stopwatch.GetElapsedTime(committed, sent.At);
        output.WriteLine($"wake-latency v7 ms={latency.TotalMilliseconds:F1}");
        latency.ShouldBeLessThan(Bound, "I was woken: its row did not wait the 8 s idle delay");

        await Eventually.HoldsAsync(() => claims.Completed(dbJ) >= 4, "J claims again");
        var ofJ = claims.Of(dbJ);
        var gap = ofJ[2].Until(ofJ[3]);
        output.WriteLine($"v7 sibling next-claim-after-ms={gap.TotalMilliseconds:F1} idle-ms={IdleWait.TotalMilliseconds}");
        gap.ShouldBeGreaterThanOrEqualTo(IdleWait - Tolerance(IdleWait), "J was not woken: its next claim was its idle one");
    }

    [DockerRequiredFact]
    public async Task APublicationFromAHostScope_ToAPartitionedStore_ThrowsNothing_AndIsPublishedByPolling()
    {
        await using var host = Build(maxPolling: TimeSpan.FromSeconds(1));
        host.Output = output;
        var ids = await host.ProvisionAsync(server, 1);
        host.Declare(ids);
        await host.StartAsync();

        var published = Guid.Empty;
        await Should.NotThrowAsync(async () => published = await host.PublishTransportAsync(ids[0]));

        await Eventually.HoldsAsync(() => host.Recorder.Sent.Any(s => s.MessageId == published), "the row is sent by the next poll");
    }

    private static PartitionedHost Build(TimeSpan maxPolling) => PartitionedHost.Build(
        bus: static b => b.AddHandler<RepublishingHandler>(),
        services: static s =>
        {
            s.AddSingleton<ClaimTimeline>();
            s.AddSingleton<Republished>();

            // Registered after the host's counting decorator: the last keyed registration is resolved.
            s.AddKeyedScoped<IOutboxProcessorStore>(Store, static (sp, _) => new TimelineOutboxStore(
                sp.GetRequiredService<EfOutboxStore<TestMessagingDbContext>>(),
                InstanceCatalog.DatabaseOf(sp.GetRequiredService<InstanceConnection>().Value),
                sp.GetRequiredService<ClaimTimeline>(),
                TimelineOutboxStore.WakeOf(sp, Store)));
        },
        options: o =>
        {
            o.Outbox.PollingInterval = Polling;
            o.Outbox.MaxPollingInterval = maxPolling;
        });

    private static MessageEnvelope Envelope(Guid id, string tenant) => new()
    {
        Id = id.ToString(),
        Source = "tests.commit-wake.upstream",
        Type = InstanceTrigger.WireType,
        Time = DateTimeOffset.UtcNow,
        DataContentType = "application/json",
        Headers = new Dictionary<string, string>(StringComparer.Ordinal) { [MessageHeaders.TenantId] = tenant },
        Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new InstanceTrigger(id), JsonSerializerOptions.Web)),
    };
}

/// <summary>A delivery whose handler publishes, so its row is enlisted in the delivery's instance.</summary>
[MessageContract("tests.commit-wake.instance-trigger")]
public sealed record InstanceTrigger(Guid Id) : IIntegrationEvent
{
    public const string WireType = "tests.commit-wake.instance-trigger.v1";
}

/// <summary>Which publication each trigger's handler made.</summary>
public sealed class Republished
{
    public ConcurrentDictionary<Guid, Guid> Of { get; } = new();
}

public sealed class RepublishingHandler(IMessageBus bus, Republished republished) : IMessageHandler<InstanceTrigger>
{
    public async ValueTask HandleAsync(InstanceTrigger message, MessageContext context, CancellationToken ct = default)
    {
        var id = await bus.PublishAsync(new InstanceEvent(Guid.NewGuid()), ct: ct);
        republished.Of[message.Id] = id.Value;
    }
}
