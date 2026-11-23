using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MicroKit.Messaging.IntegrationTests.PostgreSql.Instances;
using MicroKit.Messaging.Options;
using MicroKit.Messaging.Stores;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// Every outbox claim of a test (#215), per database: when its loop's claim began (the floor's origin,
/// <see cref="LoopWake.ClaimStarted"/>), when the store was entered and returned, and how many rows it
/// claimed. A test waits for claims to have finished — counted, never guessed.
/// </summary>
public sealed class ClaimTimeline
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ClaimRecord>> _claims = new(StringComparer.Ordinal);

    /// <summary>Gets how many claims of <paramref name="database"/> have returned.</summary>
    public int Completed(string database) => _claims.TryGetValue(database, out var claims) ? claims.Count : 0;

    /// <summary>Gets the claims of <paramref name="database"/> that returned, in order.</summary>
    public ClaimRecord[] Of(string database) => _claims.TryGetValue(database, out var claims) ? [.. claims] : [];

    internal void Add(string database, ClaimRecord claim) => _claims.GetOrAdd(database, static _ => new()).Enqueue(claim);
}

/// <summary>One claim: Stopwatch timestamps, and the rows it claimed.</summary>
public readonly record struct ClaimRecord(long ClaimStarted, long Entered, long Returned, int Claimed)
{
    /// <summary>Gets the time from this claim's return to <paramref name="next"/>'s entry.</summary>
    public TimeSpan Until(ClaimRecord next) => Stopwatch.GetElapsedTime(Returned, next.Entered);
}

/// <summary>The EF store's claim, recorded on a <see cref="ClaimTimeline"/>; settlement unchanged.</summary>
internal sealed class TimelineOutboxStore(
    EfOutboxStore<TestMessagingDbContext> inner, string database, ClaimTimeline timeline, LoopWake? wake) : IOutboxProcessorStore
{
    public async ValueTask<OutboxClaim> ClaimBatchAsync(
        int batchSize, TimeSpan lockDuration, OutboxEndpoints excluded,
        IReadOnlyList<string> excludedTransportTenants, IReadOnlyList<string> excludedLocalTenants, CancellationToken ct = default)
    {
        var entered = Stopwatch.GetTimestamp();
        var claimStarted = wake?.ClaimStarted ?? entered;
        var claim = await ((IOutboxProcessorStore)inner).ClaimBatchAsync(
            batchSize, lockDuration, excluded, excludedTransportTenants, excludedLocalTenants, ct);
        timeline.Add(database, new ClaimRecord(claimStarted, entered, Stopwatch.GetTimestamp(), claim.Messages.Count));
        return claim;
    }

    public ValueTask<int> ApplyOutcomesAsync(Guid claimToken, IReadOnlyList<OutboxOutcome> outcomes, CancellationToken ct = default)
        => ((IOutboxProcessorStore)inner).ApplyOutcomesAsync(claimToken, outcomes, ct);

    /// <summary>The wake of the loop claiming in this scope: the instance's, else the single store's.</summary>
    public static LoopWake? WakeOf(IServiceProvider services, MessageStoreKey store)
        => services.GetService<DeliveryStoreHolder>()?.Instance?.OutboxWake
            ?? services.GetRequiredService<OutboxWakes>().Of(store);
}

/// <summary>What the commit-wake hosts saw: each send, each local delivery, at which Stopwatch timestamp.</summary>
public sealed class WakeRecorder
{
    public ConcurrentDictionary<Guid, long> Sent { get; } = new();

    public ConcurrentDictionary<Guid, long> Delivered { get; } = new();

    /// <summary>Gets or sets whether the transport refuses every send, as a down broker does.</summary>
    public bool TransportDown { get; set; }
}

/// <summary>A transport that records each send, or refuses it while <see cref="WakeRecorder.TransportDown"/>.</summary>
public sealed class WakeTransport(WakeRecorder recorder) : IMessageTransport
{
    public ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        if (recorder.TransportDown)
        {
            throw new OutboxTransportUnavailableException("The broker is down (test).");
        }

        recorder.Sent.TryAdd(Guid.Parse(envelope.Id), Stopwatch.GetTimestamp());
        return ValueTask.CompletedTask;
    }
}

[MessageContract("tests.commit-wake.event")]
public sealed record WakeEvent(Guid Id) : IIntegrationEvent;

/// <summary>A local message, as a domain event's notification is.</summary>
public sealed record WakeNotice(Guid Id);

/// <summary>A delivery whose handler saves its publication inside its attempt, then fails the attempt.</summary>
[MessageContract("tests.commit-wake.save-then-throw")]
public sealed record SaveThenThrow(Guid Id) : IIntegrationEvent
{
    public const string WireType = "tests.commit-wake.save-then-throw.v1";
}

/// <summary>Publishes, saves inside the engine's attempt, then throws: the attempt rolls back uncommitted.</summary>
public sealed class SaveThenThrowHandler(IMessageBus bus, TestMessagingDbContext db) : IMessageHandler<SaveThenThrow>
{
    public async ValueTask HandleAsync(SaveThenThrow message, MessageContext context, CancellationToken ct = default)
    {
        await bus.PublishAsync(new WakeEvent(message.Id), ct: ct);
        await db.SaveChangesAsync(ct);
        throw new PermanentMessageException("The handler fails after its save (test): the attempt rolls back.");
    }
}

internal sealed class WakeNoticeConsumers : ILocalConsumerSource
{
    public IEnumerable<LocalConsumer> GetConsumers() =>
    [
        new LocalConsumer(
            typeof(WakeNotice),
            "tests.commit-wake.local",
            typeof(WakeNoticeConsumers),
            static (services, message, _, _) =>
            {
                services.GetRequiredService<WakeRecorder>().Delivered.TryAdd(((WakeNotice)message).Id, Stopwatch.GetTimestamp());
                return ValueTask.CompletedTask;
            }),
    ];
}

/// <summary>
/// A host with one single EF store on PostgreSQL, the recording transport, a local consumer, and every
/// outbox claim on a <see cref="ClaimTimeline"/> (#215).
/// </summary>
internal sealed class WakeHost : IAsyncDisposable
{
    public const string Database = "single";
    public static readonly MessageStoreKey Store = EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>();

    private readonly IHost _host;
    private readonly StoreDatabase _db;
    private bool _stopped;

    private WakeHost(IHost host, StoreDatabase db, CapturingLoggerProvider logs)
    {
        _host = host;
        _db = db;
        Logs = logs;
    }

    public IServiceProvider Services => _host.Services;

    public CapturingLoggerProvider Logs { get; }

    public WakeRecorder Recorder => Services.GetRequiredService<WakeRecorder>();

    public ClaimTimeline Claims => Services.GetRequiredService<ClaimTimeline>();

    public string ConnectionString => _db.ConnectionString;

    public static async Task<WakeHost> StartAsync(
        string serverConnectionString, TimeSpan polling, TimeSpan maxPolling, Action<MessagingOptions>? options = null)
    {
        var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(serverConnectionString, o => new(o));
        var logs = new CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.AddProvider(logs).SetMinimumLevel(LogLevel.Debug);
        builder.Services.AddSingleton<WakeRecorder>();
        builder.Services.AddSingleton<ClaimTimeline>();
        builder.Services.AddSingleton<IMessageTransport, WakeTransport>();
        builder.Services.AddDbContext<TestMessagingDbContext>(o => o.UseNpgsql(db.ConnectionString));
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.commit-wake";
            bus.Publishes<WakeEvent>();
            bus.AddHandler<SaveThenThrowHandler>();
            bus.AddLocalConsumerSource(new WakeNoticeConsumers());
            bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
            bus.Configure(o =>
            {
                o.Outbox.PollingInterval = polling;
                o.Outbox.MaxPollingInterval = maxPolling;
                o.Outbox.BatchSize = 1000;
                o.Outbox.RetentionDays = 0;
                o.Inbox.RetentionDays = 0;
                o.Inbox.MaxPollingInterval = TimeSpan.FromMinutes(10);
                options?.Invoke(o);
            });
        });

        // Registered after the EF store's: the last keyed registration is the one resolved.
        builder.Services.AddKeyedScoped<IOutboxProcessorStore>(Store, static (sp, _) => new TimelineOutboxStore(
            sp.GetRequiredService<EfOutboxStore<TestMessagingDbContext>>(),
            Database,
            sp.GetRequiredService<ClaimTimeline>(),
            TimelineOutboxStore.WakeOf(sp, Store)));

        var host = builder.Build();
        await host.StartAsync();
        return new WakeHost(host, db, logs);
    }

    /// <summary>Waits until <paramref name="count"/> claims have returned: the loop is in the wait after the last.</summary>
    public Task ClaimedAsync(int count, string because) => Eventually.HoldsAsync(() => Claims.Completed(Database) >= count, because);

    public Task StopAsync()
    {
        _stopped = true;
        return _host.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stopped)
        {
            await _host.StopAsync();
        }

        _host.Dispose();
        await _db.DisposeAsync();
    }
}
