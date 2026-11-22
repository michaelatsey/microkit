using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Primitives;
using MicroKit.Messaging.Stores;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// A partitioned store with no database (#191): its composition, its source, the registry, the gate and
/// the back-off state the engine creates for an instance — for the reconciler's and the loops' unit tests.
/// </summary>
internal sealed class InstanceHarness : IDisposable
{
    public static readonly MessageStoreKey Store = new("tests:partitioned");

    private readonly ServiceProvider _provider;

    public InstanceHarness(int permits = 4, bool tagInstances = false, Action<IServiceCollection>? configure = null)
    {
        Composition = new MessagingComposition();
        Composition.GetOrAddStore(Store, out _).PartitionCalls = 1;

        var services = new ServiceCollection();
        services.AddSingleton(Source);
        configure?.Invoke(services);
        services.TryAddKeyedSingleton<IMessageStoreInstanceSource>(Store, (sp, _) => sp.GetRequiredService<TestInstanceSource>());
        services.AddKeyedScoped<IMessageStoreInstanceInitializer, RecordingInstanceInitializer>(Store);
        services.AddSingleton(Initialized);
        services.AddScoped<DeliveryStoreHolder>();
        _provider = services.BuildServiceProvider();

        InstanceOptions = new StoreInstanceOptions { MaxConcurrentBatches = permits, TagInstances = tagInstances };
        Registry = new StoreInstanceRegistry(Composition, _provider, NullLogger<StoreInstanceRegistry>.Instance);
        Gate = new FifoGate(permits);
        Inbox = new InboxTenantBackoff(InboxOptions);
        Metrics = new OutboxMetrics(TimeProvider.System, OutboxOptions, InstanceOptions, Registry);
    }

    public MessagingComposition Composition { get; }

    public TestInstanceSource Source { get; } = new();

    public ConcurrentQueue<string> Initialized { get; } = new();

    public IServiceProvider Provider => _provider;

    public IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();

    public StoreInstanceOptions InstanceOptions { get; }

    public OutboxProcessorOptions OutboxOptions { get; } = new()
    {
        PollingInterval = TimeSpan.FromMilliseconds(5),
        MaxPollingInterval = TimeSpan.FromMilliseconds(20),
        TransportUnavailableBackoff = TimeSpan.FromMilliseconds(20),
    };

    public InboxProcessorOptions InboxOptions { get; } = new();

    public StoreInstanceRegistry Registry { get; }

    public FifoGate Gate { get; }

    public InboxTenantBackoff Inbox { get; }

    public OutboxMetrics Metrics { get; }

    /// <summary>The state the reconciler creates for an instance, registered as drained.</summary>
    public StoreInstanceState Start(string id, CancellationToken stoppingToken = default)
    {
        var key = new StoreInstanceKey(Store, id);
        var outbox = new OutboxEndpointBackoff(OutboxOptions.PollingInterval, OutboxOptions.TransportUnavailableBackoff);
        Metrics.Track(key, outbox);
        var state = new StoreInstanceState(
            Store, new MessageStoreInstance(id), outbox, Inbox.Add(key), Gate, Registry, InstanceOptions.TagInstances, stoppingToken);
        Registry.Add(state);
        return state;
    }

    public StoreInstanceReconciler Reconciler(Func<StoreInstanceState, CancellationToken, Task> runLoops, ILogger? logger = null)
        => new(
            Store,
            Registry,
            Inbox,
            Metrics,
            OutboxOptions,
            InstanceOptions,
            Gate,
            runLoops,
            TimeProvider.System,
            logger ?? NullLogger.Instance);

    public void Dispose()
    {
        Metrics.Dispose();
        _provider.Dispose();
    }
}

/// <summary>A source whose snapshot a test replaces; each replacement fires the previous token.</summary>
internal sealed class TestInstanceSource : IMessageStoreInstanceSource, IDisposable
{
    private readonly Lock _lock = new();
    private MessageStoreInstance[] _instances = [];
    private CancellationTokenSource _change = new();

    private int _version;
    private int _readVersion;

    private Exception? _throwOnRead;

    /// <summary>Gets or sets an exception the next snapshot throws.</summary>
    public Exception? ThrowOnRead
    {
        get
        {
            lock (_lock)
            {
                return _throwOnRead;
            }
        }

        set
        {
            lock (_lock)
            {
                _throwOnRead = value;
            }
        }
    }

    /// <summary>Gets the version of the latest snapshot set: one per <see cref="Replace"/>.</summary>
    public int Version
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    /// <summary>Gets the version of the latest snapshot a reader was handed.</summary>
    public int ReadVersion
    {
        get
        {
            lock (_lock)
            {
                return _readVersion;
            }
        }
    }

    public void Set(params string[] ids) => Replace([.. ids.Select(static id => new MessageStoreInstance(id))]);

    public void Replace(MessageStoreInstance[] instances)
    {
        CancellationTokenSource previous;
        lock (_lock)
        {
            _instances = instances;
            _version++;
            previous = _change;
            _change = new CancellationTokenSource();
        }

        previous.Cancel();
        previous.Dispose();
    }

    public IReadOnlyCollection<MessageStoreInstance> GetInstances()
    {
        lock (_lock)
        {
            if (_throwOnRead is { } fault)
            {
                _throwOnRead = null;
                throw fault;
            }

            _readVersion = _version;
            return _instances;
        }
    }

    public IChangeToken GetChangeToken()
    {
        lock (_lock)
        {
            return new CancellationChangeToken(_change.Token);
        }
    }

    public void Dispose() => _change.Dispose();

    public bool TryGetInstance(MessageContext context, [NotNullWhen(true)] out MessageStoreInstance? instance)
    {
        instance = context.TenantId is { } tenant ? new MessageStoreInstance(tenant) : null;
        return instance is not null;
    }
}

/// <summary>Records the instance each prepared scope served.</summary>
internal sealed class RecordingInstanceInitializer(ConcurrentQueue<string> initialized) : IMessageStoreInstanceInitializer
{
    public ValueTask InitializeAsync(MessageStoreInstance instance, IServiceProvider services, CancellationToken ct = default)
    {
        initialized.Enqueue(instance.Id);
        return ValueTask.CompletedTask;
    }
}
