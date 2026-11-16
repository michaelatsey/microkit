using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// A host over one PostgreSQL store, for the #188 D6 suites: the guarded handlers of
/// <see cref="GuardEvent"/>, a commit fault injector, and optionally a retrying execution strategy
/// and the in-memory transport. Built and started by the test; several hosts may share one database.
/// </summary>
internal sealed class GuardHost : IAsyncDisposable
{
    public const string ServiceName = "tests.guard";

    private readonly IHost _host;
    private bool _started;

    private GuardHost(IHost host, StoreDatabase database)
    {
        _host = host;
        Database = database;
    }

    public static readonly MessageStoreKey Store = EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>();

    public IServiceProvider Provider => _host.Services;

    public StoreDatabase Database { get; }

    public GuardProbe Probe => Provider.GetRequiredService<GuardProbe>();

    public CommitFaultInjector Faults => Provider.GetRequiredService<CommitFaultInjector>();

    public FailedRowInsertBlocker FailedRowInserts => Provider.GetRequiredService<FailedRowInsertBlocker>();

    public CapturingLoggerProvider Logs => Provider.GetRequiredService<CapturingLoggerProvider>();

    /// <summary>Builds a host; nothing starts until <see cref="StartAsync"/>.</summary>
    /// <param name="database">The store's database.</param>
    /// <param name="handlers">Registers the handlers; both guard handlers when omitted.</param>
    /// <param name="retrying">Whether the store's context retries transient faults.</param>
    /// <param name="broker">The in-memory broker; no transport when omitted.</param>
    /// <param name="lease">The inbox lease.</param>
    /// <param name="maxRetries">The inbox attempts.</param>
    /// <param name="clock">The host's clock; the system clock when omitted.</param>
    public static GuardHost Create(
        StoreDatabase database,
        Action<MessagingBuilder>? handlers = null,
        bool retrying = false,
        InMemoryBroker? broker = null,
        TimeSpan? lease = null,
        int maxRetries = 5,
        TimeProvider? clock = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        var logs = new CapturingLoggerProvider();
        builder.Logging.SetMinimumLevel(LogLevel.Debug).AddProvider(logs);
        builder.Services.AddSingleton(logs);
        builder.Services.AddSingleton<GuardProbe>();
        builder.Services.AddSingleton<CommitFaultInjector>();
        builder.Services.AddSingleton<FailedRowInsertBlocker>();
        if (clock is not null)
        {
            builder.Services.AddSingleton(clock);
        }

        builder.Services.AddDbContext<TestMessagingDbContext>((sp, o) => o
            .UseNpgsql(database.ConnectionString, npgsql =>
            {
                if (retrying)
                {
                    npgsql.ExecutionStrategy(d => new FaultRetryingStrategy(d));
                }
            })
            .AddInterceptors(
                sp.GetRequiredService<CommitFaultInjector>(),
                sp.GetRequiredService<FailedRowInsertBlocker>()));

        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = ServiceName;
            bus.Publishes<GuardEvent>();
            bus.Publishes<GuardFollowUp>();
            bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
            if (broker is not null)
            {
                bus.UseInMemoryTransport(broker);
            }

            if (handlers is null)
            {
                bus.AddHandler<FirstGuardHandler>().AddHandler<SecondGuardHandler>();
            }
            else
            {
                handlers(bus);
            }

            bus.Configure(o =>
            {
                o.Outbox.PollingInterval = TimeSpan.FromMilliseconds(50);
                o.Inbox.PollingInterval = TimeSpan.FromMilliseconds(50);
                o.Inbox.LeaseDuration = lease ?? TimeSpan.FromMinutes(5);
                o.Inbox.MaxRetries = maxRetries;
                // Due at once: a test drives each retry pass itself.
                o.Inbox.MaxRetryBackoff = TimeSpan.FromTicks(1);
                o.Outbox.RetentionDays = 0;
                o.Inbox.RetentionDays = 0;
            });
        });

        return new GuardHost(builder.Build(), database);
    }

    public async Task StartAsync()
    {
        await _host.StartAsync();
        _started = true;
    }

    public ValueTask<EnvelopeReceiveResult> ReceiveAsync(MessageEnvelope envelope)
        => Provider.GetRequiredService<IEnvelopeReceiver>().ReceiveAsync(envelope);

    /// <summary>One pass of the store's retry loop.</summary>
    public async Task<InboxBatchResult> RetryOnceAsync()
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredKeyedService<IInboxProcessor>(Store).ProcessBatchAsync(10);
    }

    public TestMessagingDbContext NewContext()
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(Database.ConnectionString).Options);

    public async Task<List<InboxMessage>> InboxAsync()
    {
        await using var context = NewContext();
        return await context.InboxMessages.AsNoTracking().ToListAsync();
    }

    public async Task<List<string>> EffectsAsync()
    {
        await using var context = NewContext();
        return await context.BusinessRecords.AsNoTracking().Select(r => r.Value).ToListAsync();
    }

    public async Task<List<OutboxMessage>> OutboxAsync()
    {
        await using var context = NewContext();
        return await context.OutboxMessages.AsNoTracking().ToListAsync();
    }

    /// <summary>Seeds the Failed row of a handler, as a failed first delivery leaves it.</summary>
    public async Task<InboxMessage> SeedFailedAsync<THandler>(Guid messageId, GuardEvent message, int retryCount = 1)
    {
        var row = new InboxMessage
        {
            MessageId = MessageId.From(messageId),
            ConsumerType = typeof(THandler).AssemblyQualifiedName!,
            EventType = typeof(GuardEvent).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(message, JsonSerializerOptions.Web),
            Source = ServiceName,
            Status = InboxMessageStatus.Failed,
            RetryCount = retryCount,
            ReceivedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            OccurredOnUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        await using var context = NewContext();
        context.InboxMessages.Add(row);
        await context.SaveChangesAsync();
        return row;
    }

    public static MessageEnvelope Envelope(Guid messageId, GuardEvent message, string source = ServiceName) => new()
    {
        Id = messageId.ToString(),
        Source = source,
        Type = GuardEvent.WireType,
        Time = DateTimeOffset.UtcNow,
        DataContentType = "application/json",
        Headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MessageHeaders.CorrelationId] = Guid.NewGuid().ToString(),
        },
        Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonSerializerOptions.Web)),
    };

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            try
            {
                await _host.StopAsync();
            }
            catch (OperationCanceledException)
            {
                // Stopped under a test's own cancellation.
            }
        }

        _host.Dispose();
    }
}

[MessageContract(Name)]
public sealed record GuardEvent(Guid Id) : IIntegrationEvent
{
    public const string Name = "tests.guard.event";

    public const string WireType = Name + ".v1";
}

/// <summary>What a guarded handler publishes: its cascade, which must commit with it, once.</summary>
[MessageContract("tests.guard.follow-up")]
public sealed record GuardFollowUp(Guid Id, string Handler) : IIntegrationEvent;

/// <summary>One call of a guarded handler, handed to the test's script.</summary>
public sealed record GuardCall(
    string Handler, int Call, GuardEvent Message, TestMessagingDbContext Context, IMessageBus Bus, CancellationToken Ct);

/// <summary>Counts each handler's calls and runs the test's script inside them.</summary>
public sealed class GuardProbe
{
    private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);

    /// <summary>Gets the script each handler runs after writing its effect, by handler name.</summary>
    public ConcurrentDictionary<string, Func<GuardCall, Task>> Scripts { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets whether the handlers publish a <see cref="GuardFollowUp"/>.</summary>
    public bool PublishFollowUp { get; set; }

    public int Calls(string handler) => _calls.GetValueOrDefault(handler);

    internal int Next(string handler) => _calls.AddOrUpdate(handler, 1, (_, n) => n + 1);
}

/// <summary>
/// Writes its effect — a business record named after itself and the message — through the store's
/// context, optionally publishes its follow-up, then runs the test's script.
/// </summary>
public abstract class GuardHandlerBase(GuardProbe probe, TestMessagingDbContext store, IMessageBus bus)
    : IMessageHandler<GuardEvent>
{
    protected abstract string Name { get; }

    public async ValueTask HandleAsync(GuardEvent message, MessageContext context, CancellationToken ct = default)
    {
        var call = probe.Next(Name);
        store.BusinessRecords.Add(new BusinessRecord { Id = Guid.NewGuid(), Value = $"{Name}:{message.Id}" });

        if (probe.PublishFollowUp)
        {
            await bus.PublishAsync(new GuardFollowUp(message.Id, Name), ct: ct);
        }

        if (probe.Scripts.TryGetValue(Name, out var script))
        {
            await script(new GuardCall(Name, call, message, store, bus, ct));
        }
    }
}

public sealed class FirstGuardHandler(GuardProbe probe, TestMessagingDbContext context, IMessageBus bus)
    : GuardHandlerBase(probe, context, bus)
{
    public const string HandlerName = "first";

    protected override string Name => HandlerName;
}

public sealed class SecondGuardHandler(GuardProbe probe, TestMessagingDbContext context, IMessageBus bus)
    : GuardHandlerBase(probe, context, bus)
{
    public const string HandlerName = "second";

    protected override string Name => HandlerName;
}
