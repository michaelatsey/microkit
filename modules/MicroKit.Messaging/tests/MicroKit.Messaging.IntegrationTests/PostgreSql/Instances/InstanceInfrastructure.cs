using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using MicroKit.Messaging.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql.Instances;

/// <summary>
/// One PostgreSQL server holding the databases of a partitioned store's instances (#191). Its own
/// container, not the shared <see cref="PostgreSqlFixture"/>: it starts with a connection budget it states
/// and holds (A5).
/// </summary>
/// <remarks>
/// <para>
/// <b>The connection budget.</b> Npgsql keeps one pool per connection string — one per database — and an
/// idle connection stays pooled for <c>Connection Idle Lifetime</c>. Every database's connection string
/// caps its pool at <see cref="PoolSizePerDatabase"/> and prunes idle connections after
/// <see cref="IdleLifetimeSeconds"/>; the server allows <see cref="MaxConnections"/>. With at most
/// <see cref="MaxDatabases"/> databases open at once, the pools can never exceed the server
/// (50 × 10 = 500 &lt; 600 − the admin connections).
/// </para>
/// <para>
/// Each database is cloned from a template carrying the schema (<c>CREATE DATABASE … TEMPLATE</c>), the
/// way a host provisions a tenant's database before declaring it (D8).
/// </para>
/// </remarks>
public sealed class InstanceServerFixture : IAsyncLifetime, IDisposable
{
    public const int MaxConnections = 600;
    public const int PoolSizePerDatabase = 10;
    public const int IdleLifetimeSeconds = 3;
    public const int MaxDatabases = 50;
    private const string Template = "instances_template";

    private readonly SemaphoreSlim _creating = new(1, 1);
    private PostgreSqlContainer? _container;

    public string ServerConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException("The instances' PostgreSQL server is not running: mark the test [DockerRequiredFact].");

    public async Task InitializeAsync()
    {
        if (!DockerEndpoint.ShouldRun)
        {
            return;
        }

        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("microkit_messaging_instances")
            .WithCommand("-c", $"max_connections={MaxConnections}")
            .Build();
        await _container.StartAsync();

        await ExecuteAsync($"CREATE DATABASE \"{Template}\"");
        var template = ConnectionStringOf(Template);
        await using (var context = new TestMessagingDbContext(
            new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(template).Options))
        {
            await context.Database.EnsureCreatedAsync();
        }

        // A template is cloned only while no session is open on it.
        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>A new database with the messaging schema; its connection string carries the pool budget.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"inst_{Guid.NewGuid():N}";
        await _creating.WaitAsync();
        try
        {
            await ExecuteAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{Template}\"");
        }
        finally
        {
            _creating.Release();
        }

        return ConnectionStringOf(name);
    }

    /// <summary>Refuses every new connection to <paramref name="connectionString"/>'s database and ends its sessions.</summary>
    public async Task StopDatabaseAsync(string connectionString)
    {
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        await ExecuteAsync($"ALTER DATABASE \"{name}\" WITH ALLOW_CONNECTIONS false");
        await ExecuteAsync($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{name}'");
        using var pooled = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(pooled);
    }

    /// <summary>The client connections open on the server now, every database included.</summary>
    public async Task<int> ClientConnectionsAsync()
    {
        await using var connection = new NpgsqlConnection(ServerConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM pg_stat_activity WHERE backend_type = 'client backend'";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose() => _creating.Dispose();

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await _container.DisposeAsync();
        }
    }

    private string ConnectionStringOf(string database) => new NpgsqlConnectionStringBuilder(ServerConnectionString)
    {
        Database = database,
        MaxPoolSize = PoolSizePerDatabase,
        ConnectionIdleLifetime = IdleLifetimeSeconds,
        ConnectionPruningInterval = 1,
    }.ConnectionString;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ServerConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>Samples the server's client connections while a test runs, and keeps the peak (A5).</summary>
internal sealed class ConnectionSampler : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sampling;
    private int _peak;

    public ConnectionSampler(InstanceServerFixture server)
        => _sampling = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    var now = await server.ClientConnectionsAsync();
                    InterlockedMax(ref _peak, now);
                    await Task.Delay(100, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });

    public int Peak => Volatile.Read(ref _peak);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _sampling;
        _stop.Dispose();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current && Interlocked.CompareExchange(ref target, value, current) is var seen && seen != current)
        {
            current = seen;
        }
    }
}

/// <summary>The instance ids a test declares, per store, and each one's connection string.</summary>
public sealed class InstanceCatalog
{
    private readonly ConcurrentDictionary<string, string> _databases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<MessageStoreKey, Snapshot> _sets = new();

    public void Register(string id, string connectionString) => _databases[id] = connectionString;

    public string ConnectionStringOf(string id) => _databases[id];

    public static string DatabaseOf(string? connectionString)
        => connectionString is null ? "<none>" : new NpgsqlConnectionStringBuilder(connectionString).Database ?? "<none>";

    public string DatabaseOfInstance(string id) => DatabaseOf(_databases[id]);

    /// <summary>Replaces <paramref name="store"/>'s instances and fires its change token.</summary>
    public void Set(MessageStoreKey store, params string[] ids) => SetOf(store).Replace(ids);

    internal Snapshot SetOf(MessageStoreKey store) => _sets.GetOrAdd(store, static _ => new Snapshot());

    internal sealed class Snapshot : IDisposable
    {
        private readonly Lock _lock = new();
        private MessageStoreInstance[] _instances = [];
        private CancellationTokenSource _change = new();

        public MessageStoreInstance[] Instances
        {
            get
            {
                lock (_lock)
                {
                    return _instances;
                }
            }
        }

        public IChangeToken Token
        {
            get
            {
                lock (_lock)
                {
                    return new CancellationChangeToken(_change.Token);
                }
            }
        }

        public void Dispose() => _change.Dispose();

        public void Replace(string[] ids)
        {
            CancellationTokenSource previous;
            lock (_lock)
            {
                _instances = [.. ids.Select(static id => new MessageStoreInstance(id))];
                previous = _change;
                _change = new CancellationTokenSource();
            }

            previous.Cancel();
            previous.Dispose();
        }
    }
}

/// <summary>How a test maps a delivery to an instance: by its tenant, by default.</summary>
public sealed class InstanceMapping
{
    public Func<MessageContext, string?> Map { get; set; } = static context => context.TenantId;
}

/// <summary>
/// The source of the partitioned test store: its instances are what the catalog holds for its key. A
/// second partitioned store has a source type of its own (<see cref="BareInstanceSource"/>).
/// </summary>
public class TestInstanceSource(InstanceCatalog catalog, InstanceMapping mapping) : IMessageStoreInstanceSource
{
    private InstanceCatalog.Snapshot Set => catalog.SetOf(Store);

    /// <summary>Gets the store this source serves.</summary>
    protected virtual MessageStoreKey Store => PartitionedHost.Store;

    public virtual IReadOnlyCollection<MessageStoreInstance> GetInstances() => Set.Instances;

    public IChangeToken GetChangeToken() => Set.Token;

    public bool TryGetInstance(MessageContext context, [NotNullWhen(true)] out MessageStoreInstance? instance)
    {
        instance = mapping.Map(context) is { } id ? new MessageStoreInstance(id) : null;
        return instance is not null;
    }
}

/// <summary>A source whose first snapshot read blocks until the test opens it: a slow first read of a catalog.</summary>
public sealed class GatedInstanceSource(InstanceCatalog catalog, InstanceMapping mapping) : TestInstanceSource(catalog, mapping)
{
    private int _reads;

    /// <summary>Gets the gate the first read waits on.</summary>
    public ManualResetEventSlim FirstRead { get; } = new();

    /// <summary>Gets a value indicating whether the first read has started.</summary>
    public bool FirstReadStarted => Volatile.Read(ref _reads) > 0;

    public override IReadOnlyCollection<MessageStoreInstance> GetInstances()
    {
        if (Interlocked.Increment(ref _reads) == 1)
        {
            FirstRead.Wait(TimeSpan.FromSeconds(30));
        }

        return base.GetInstances();
    }
}

/// <summary>The source of V9's second partitioned store.</summary>
public sealed class BareInstanceSource(InstanceCatalog catalog, InstanceMapping mapping) : TestInstanceSource(catalog, mapping)
{
    protected override MessageStoreKey Store => EntityFrameworkOutbox.KeyOf<PostgreSql.Instances.BareDbContext>();
}

/// <summary>The scope's connection: unset outside an instance, so the context cannot be built there.</summary>
public sealed class InstanceConnection
{
    public string? Value { get; set; }

    public string Required => Value ?? throw new InvalidOperationException(
        "This scope serves no store instance: the context's database is only known inside an instance's scope.");
}

/// <summary>Points the scope's connection at the instance's database (D3), the way #190 will for a tenant.</summary>
public sealed class TestInstanceInitializer(InstanceConnection connection, InstanceCatalog catalog, InstanceRecorder recorder)
    : IMessageStoreInstanceInitializer
{
    public ValueTask InitializeAsync(MessageStoreInstance instance, IServiceProvider services, CancellationToken ct = default)
    {
        if (recorder.FailInitializerFor is { } failing && failing == instance.Id)
        {
            throw new InvalidOperationException($"The connection of instance '{instance.Id}' cannot be resolved.");
        }

        connection.Value = catalog.ConnectionStringOf(instance.Id);
        return ValueTask.CompletedTask;
    }
}

/// <summary>What the host did, per database: sends, handled messages, claims.</summary>
public sealed class InstanceRecorder
{
    public ConcurrentQueue<(Guid MessageId, string Database, long At)> Sent { get; } = new();

    public ConcurrentQueue<(Guid MessageId, string Database, string Consumer)> Handled { get; } = new();

    public ConcurrentDictionary<string, int> OutboxClaims { get; } = new(StringComparer.Ordinal);

    public ConcurrentQueue<(string Database, string[] ExcludedTenants)> InboxClaims { get; } = new();

    /// <summary>Gets or sets a (database, tenant) whose handler's dependency is down.</summary>
    public (string Database, string Tenant)? DependencyDown { get; set; }

    /// <summary>Gets or sets a point the next handler signals, then waits on.</summary>
    public (TaskCompletionSource Entered, TaskCompletionSource Resume)? HoldHandler { get; set; }

    /// <summary>Gets or sets an instance whose initializer throws.</summary>
    public string? FailInitializerFor { get; set; }

    /// <summary>A cost every send of these databases pays: a backlog that holds the gate for a while.</summary>
    public ConcurrentDictionary<string, TimeSpan> SendCost { get; } = new(StringComparer.Ordinal);

    /// <summary>Holds every send of a database until released.</summary>
    public ConcurrentDictionary<string, TaskCompletionSource> HeldSends { get; } = new(StringComparer.Ordinal);

    public int ClaimsOf(string database) => OutboxClaims.GetValueOrDefault(database);

    public static long Now => Stopwatch.GetTimestamp();
}

/// <summary>A transport resolved per dispatch scope: it records the database its scope was prepared for.</summary>
public sealed class ScopedRecordingTransport(InstanceConnection connection, InstanceRecorder recorder) : IMessageTransport
{
    public async ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        var database = InstanceCatalog.DatabaseOf(connection.Value);
        if (recorder.HeldSends.TryGetValue(database, out var held))
        {
            await held.Task.WaitAsync(ct);
        }

        if (recorder.SendCost.TryGetValue(database, out var cost))
        {
            await Task.Delay(cost, ct);
        }

        recorder.Sent.Enqueue((Guid.Parse(envelope.Id), database, InstanceRecorder.Now));
    }
}

/// <summary>Counts each claim of each database's outbox, and each inbox claim with the tenants it leaves out.</summary>
internal sealed class CountingOutboxStore(EfOutboxStore<TestMessagingDbContext> inner, InstanceConnection connection, InstanceRecorder recorder)
    : IOutboxProcessorStore
{
    public ValueTask<OutboxClaim> ClaimBatchAsync(
        int batchSize, TimeSpan lockDuration, OutboxEndpoints excluded,
        IReadOnlyList<string> excludedTransportTenants, IReadOnlyList<string> excludedLocalTenants, CancellationToken ct = default)
    {
        recorder.OutboxClaims.AddOrUpdate(InstanceCatalog.DatabaseOf(connection.Value), 1, static (_, n) => n + 1);
        return ((IOutboxProcessorStore)inner).ClaimBatchAsync(batchSize, lockDuration, excluded, excludedTransportTenants, excludedLocalTenants, ct);
    }

    public ValueTask<int> ApplyOutcomesAsync(Guid claimToken, IReadOnlyList<OutboxOutcome> outcomes, CancellationToken ct = default)
        => ((IOutboxProcessorStore)inner).ApplyOutcomesAsync(claimToken, outcomes, ct);
}

/// <inheritdoc cref="CountingOutboxStore"/>
internal sealed class CountingInboxStore(EfInboxStore<TestMessagingDbContext> inner, InstanceConnection connection, InstanceRecorder recorder)
    : IInboxProcessorStore
{
    public ValueTask<InboxClaim> ClaimBatchAsync(
        int batchSize, TimeSpan leaseDuration, IReadOnlyList<string> excludedTenants, CancellationToken ct = default)
    {
        recorder.InboxClaims.Enqueue((InstanceCatalog.DatabaseOf(connection.Value), [.. excludedTenants]));
        return ((IInboxProcessorStore)inner).ClaimBatchAsync(batchSize, leaseDuration, excludedTenants, ct);
    }

    public ValueTask<int> ApplyOutcomesAsync(Guid claimToken, IReadOnlyList<InboxOutcome> outcomes, CancellationToken ct = default)
        => ((IInboxProcessorStore)inner).ApplyOutcomesAsync(claimToken, outcomes, ct);
}

[MessageContract("tests.instances.event")]
public sealed record InstanceEvent(Guid Id) : IIntegrationEvent
{
    public const string WireType = "tests.instances.event.v1";
}

public sealed record InstanceNotice(Guid Id);

/// <summary>Handles <see cref="InstanceEvent"/>: records the database its context was built for.</summary>
public sealed class InstanceHandler(TestMessagingDbContext db, InstanceRecorder recorder) : IMessageHandler<InstanceEvent>
{
    public async ValueTask HandleAsync(InstanceEvent message, MessageContext context, CancellationToken ct = default)
    {
        var database = db.Database.GetDbConnection().Database;
        if (recorder.HoldHandler is { } hold)
        {
            recorder.HoldHandler = null;
            hold.Entered.TrySetResult();
            await hold.Resume.Task.WaitAsync(ct);
        }

        if (recorder.DependencyDown is { } down && down.Database == database && down.Tenant == context.TenantId)
        {
            throw new InboxDependencyUnavailableException($"Tenant {context.TenantId}'s dependency is down in {database}.");
        }

        recorder.Handled.Enqueue((message.Id, database, nameof(InstanceHandler)));
    }
}

/// <summary>The local consumer of <see cref="InstanceNotice"/>, guarded by the partitioned store.</summary>
public sealed class InstanceNoticeConsumers : ILocalConsumerSource
{
    public const string Consumer = "tests.instances.notice-consumer";

    public IEnumerable<LocalConsumer> GetConsumers() =>
    [
        new LocalConsumer(typeof(InstanceNotice), Consumer, typeof(InstanceNoticeConsumers), static (services, message, _, _) =>
        {
            var database = services.GetRequiredService<TestMessagingDbContext>().Database.GetDbConnection().Database;
            services.GetRequiredService<InstanceRecorder>().Handled.Enqueue((((InstanceNotice)message).Id, database, Consumer));
            return ValueTask.CompletedTask;
        }),
    ];
}

/// <summary>Observes the back-off counters of the given meters, with each measurement's instance tag.</summary>
internal sealed class InstanceMeterRecorder : IDisposable
{
    private readonly System.Diagnostics.Metrics.MeterListener _listener = new();
    private readonly ConcurrentQueue<(string Instrument, string? Store, string? Instance, int Value)> _observed = new();

    public InstanceMeterRecorder(params System.Diagnostics.Metrics.Meter[] meters)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (Array.IndexOf(meters, instrument.Meter) >= 0 && instrument.IsObservable)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
        {
            string? store = null, instance = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "store.key")
                {
                    store = tag.Value as string;
                }
                else if (tag.Key == "store.instance")
                {
                    instance = tag.Value as string;
                }
            }

            _observed.Enqueue((instrument.Name, store, instance, value));
        });
        _listener.Start();
    }

    public List<(string Instrument, string? Store, string? Instance, int Value)> Observed()
    {
        _observed.Clear();
        _listener.RecordObservableInstruments();
        return [.. _observed];
    }

    public void Dispose() => _listener.Dispose();
}

/// <summary>Captures every record with the logging scopes it was written in (A7).</summary>
public sealed class ScopeCapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<Entry> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<Entry> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    public sealed record Entry(string Category, LogLevel Level, int EventId, string Message, IReadOnlyDictionary<string, object?> Scope);

    private sealed class Logger(string category, ScopeCapturingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scope = new Dictionary<string, object?>(StringComparer.Ordinal);
            provider._scopes.ForEachScope(
                static (value, into) =>
                {
                    if (value is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        foreach (var (k, v) in pairs)
                        {
                            into[k] = v;
                        }
                    }
                },
                scope);
            provider._entries.Enqueue(new Entry(category, logLevel, eventId.Id, formatter(state, exception), scope));
        }
    }
}

/// <summary>A host with one partitioned EF store over <see cref="TestMessagingDbContext"/>, its databases on one server.</summary>
internal sealed class PartitionedHost : IAsyncDisposable
{
    public static readonly MessageStoreKey Store = EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>();
    private readonly IHost _host;
    private bool _started;

    private PartitionedHost(IHost host) => _host = host;

    public IServiceProvider Services => _host.Services;

    public InstanceCatalog Catalog => Services.GetRequiredService<InstanceCatalog>();

    public InstanceRecorder Recorder => Services.GetRequiredService<InstanceRecorder>();

    public ScopeCapturingLoggerProvider Logs { get; private init; } = null!;

    public static PartitionedHost Build(
        Action<MessagingBuilder>? bus = null,
        Action<IServiceCollection>? services = null,
        Action<MessagingOptions>? options = null,
        bool scopedTransport = true,
        string environment = "Production")
    {
        var logs = new ScopeCapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = environment,
        });
        if (environment == Environments.Development)
        {
            // What the default host builder turns on in Development (V10).
            builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            }));
        }

        builder.Logging.AddProvider(logs).SetMinimumLevel(LogLevel.Debug);
        builder.Services.AddSingleton<InstanceCatalog>();
        builder.Services.AddSingleton<InstanceMapping>();
        builder.Services.AddSingleton<InstanceRecorder>();
        builder.Services.AddScoped<InstanceConnection>();
        builder.Services.AddDbContext<TestMessagingDbContext>((sp, o) => o.UseNpgsql(sp.GetRequiredService<InstanceConnection>().Required));
        if (scopedTransport)
        {
            builder.Services.AddScoped<IMessageTransport, ScopedRecordingTransport>();
        }

        // Counting decorators, registered first: the EF store's TryAdd keeps them.
        builder.Services.AddKeyedScoped<IOutboxProcessorStore>(Store, static (sp, _) => new CountingOutboxStore(
            sp.GetRequiredService<EfOutboxStore<TestMessagingDbContext>>(),
            sp.GetRequiredService<InstanceConnection>(),
            sp.GetRequiredService<InstanceRecorder>()));
        builder.Services.AddKeyedScoped<IInboxProcessorStore>(Store, static (sp, _) => new CountingInboxStore(
            sp.GetRequiredService<EfInboxStore<TestMessagingDbContext>>(),
            sp.GetRequiredService<InstanceConnection>(),
            sp.GetRequiredService<InstanceRecorder>()));
        services?.Invoke(builder.Services);

        builder.Services.AddMicroKitMessaging(b =>
        {
            b.ServiceName = "tests.instances";
            b.Publishes<InstanceEvent>();
            b.AddHandler<InstanceHandler>();
            b.AddLocalConsumerSource(new InstanceNoticeConsumers());
            b.UseEntityFrameworkOutbox<TestMessagingDbContext>(s => s
                .ForAssemblyContaining<InstanceHandler>()
                .PartitionedBy<TestInstanceSource, TestInstanceInitializer>());
            b.Configure(o =>
            {
                o.Outbox.PollingInterval = TimeSpan.FromMilliseconds(50);
                o.Outbox.MaxPollingInterval = TimeSpan.FromMilliseconds(200);
                o.Outbox.TransportUnavailableBackoff = TimeSpan.FromMilliseconds(400);
                o.Inbox.PollingInterval = TimeSpan.FromMilliseconds(50);
                o.Inbox.MaxPollingInterval = TimeSpan.FromMilliseconds(200);
                o.Inbox.DependencyUnavailableBackoff = TimeSpan.FromMinutes(5);
                o.Outbox.RetentionDays = 0;
                o.Inbox.RetentionDays = 0;
                options?.Invoke(o);
            });
            bus?.Invoke(b);
        });

        return new PartitionedHost(builder.Build()) { Logs = logs };
    }

    /// <summary>Registers databases as instances of the catalog, without declaring them to the source yet.</summary>
    public async Task<string[]> ProvisionAsync(InstanceServerFixture server, int count, string prefix = "i")
    {
        var ids = new string[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = $"{prefix}{i}";
            Catalog.Register(ids[i], await server.CreateDatabaseAsync());
        }

        return ids;
    }

    public void Declare(params string[] ids) => Catalog.Set(Store, ids);

    public async Task StartAsync()
    {
        await _host.StartAsync();
        _started = true;
    }

    /// <summary>Runs <paramref name="work"/> in an application scope whose connection is the instance's database.</summary>
    public async Task InInstanceAsync(string id, Func<IServiceProvider, Task> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<InstanceConnection>().Value = Catalog.ConnectionStringOf(id);
        await work(scope.ServiceProvider);
    }

    /// <summary>Publishes a transport message and sends a local one, in one unit of work of the instance's database.</summary>
    public Task<(Guid Transport, Guid Local)> PublishAsync(string id, string? tenant = null)
        => PublishAsync(id, Guid.NewGuid(), Guid.NewGuid(), tenant);

    public async Task<(Guid Transport, Guid Local)> PublishAsync(string id, Guid transport, Guid local, string? tenant = null)
    {
        await InInstanceAsync(id, async sp =>
        {
            // The envelope carries the message id the bus mints, not the payload's.
            transport = (await sp.GetRequiredService<IMessageBus>().PublishAsync(new InstanceEvent(transport), new PublishOptions { TenantId = tenant })).Value;
            await sp.GetRequiredService<ILocalMessageSender>().SendAsync(new InstanceNotice(local), MessageId.From(local), DateTimeOffset.UtcNow);
            await sp.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
        });
        return (transport, local);
    }

    /// <summary>Publishes one transport message in the instance's database.</summary>
    public async Task<Guid> PublishTransportAsync(string id)
    {
        var message = Guid.Empty;
        await InInstanceAsync(id, async sp =>
        {
            // The envelope carries the message id the bus mints, not the payload's.
            message = (await sp.GetRequiredService<IMessageBus>().PublishAsync(new InstanceEvent(Guid.NewGuid()))).Value;
            await sp.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
        });
        return message;
    }

    /// <summary>Inserts an inbox row of <see cref="InstanceHandler"/> whose retry is due.</summary>
    public async Task<Guid> SeedDueRetryAsync(string id, string? tenant = null)
    {
        var message = Guid.NewGuid();
        await using var db = Context(id);
        db.InboxMessages.Add(new InboxMessage
        {
            MessageId = MessageId.From(message),
            ConsumerType = MessageConsumerKey.Of(typeof(InstanceHandler)),
            EventType = typeof(InstanceEvent).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(new InstanceEvent(message), JsonSerializerOptions.Web),
            Source = "tests.instances.upstream",
            TenantId = tenant,
            Status = InboxMessageStatus.Failed,
            RetryCount = 1,
            ReceivedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            OccurredOnUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await db.SaveChangesAsync();
        return message;
    }

    /// <summary>A context on the instance's database, outside the host.</summary>
    public TestMessagingDbContext Context(string id)
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(Catalog.ConnectionStringOf(id)).Options);

    public async Task<List<OutboxMessage>> OutboxOfAsync(string id)
    {
        await using var db = Context(id);
        return await db.OutboxMessages.AsNoTracking().ToListAsync();
    }

    public async Task<List<InboxMessage>> InboxOfAsync(string id)
    {
        await using var db = Context(id);
        return await db.InboxMessages.AsNoTracking().ToListAsync();
    }

    public static MessageEnvelope Envelope(Guid id, string tenant, IReadOnlyDictionary<string, string>? headers = null)
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MessageHeaders.TenantId] = tenant,
            [MessageHeaders.CorrelationId] = Guid.NewGuid().ToString(),
        };
        foreach (var (k, v) in headers ?? new Dictionary<string, string>())
        {
            all[k] = v;
        }

        return new MessageEnvelope
        {
            Id = id.ToString(),
            Source = "tests.instances.upstream",
            Type = InstanceEvent.WireType,
            Time = DateTimeOffset.UtcNow,
            DataContentType = "application/json",
            Headers = all,
            Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new InstanceEvent(id), JsonSerializerOptions.Web)),
        };
    }

    /// <summary>Gets or sets where the host's log is written when it is disposed, so a failure explains itself.</summary>
    public Xunit.Abstractions.ITestOutputHelper? Output { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _host.StopAsync(stop.Token);
        }

        _host.Dispose();
        if (Output is { } output)
        {
            foreach (var e in Logs.Entries.Where(static e => e.Level >= LogLevel.Information && !e.Category.StartsWith("Microsoft", StringComparison.Ordinal)))
            {
                var instance = e.Scope.GetValueOrDefault("Instance");
                output.WriteLine($"{e.Level} {e.EventId} [{instance}] {e.Category}: {e.Message}");
            }
        }
    }
}

internal static class Eventually
{
    public static async Task HoldsAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(60);
        while (!condition())
        {
            if (deadline.Elapsed > limit)
            {
                throw new ShouldAssertException($"Timed out after {limit}: {because}");
            }

            await Task.Delay(20);
        }
    }

    public static async Task HoldsAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(60);
        while (!await condition())
        {
            if (deadline.Elapsed > limit)
            {
                throw new ShouldAssertException($"Timed out after {limit}: {because}");
            }

            await Task.Delay(50);
        }
    }
}
