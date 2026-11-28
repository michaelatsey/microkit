using Microsoft.Extensions.Time.Testing;

using MicroKit.Messaging.Bus;
using MicroKit.Messaging.Execution;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Stores;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// A receive pipeline built by hand over a container the test fills, with one store whose
/// <see cref="IInboxGuardStore"/> is <see cref="InMemoryInboxGuard"/> — or no store at all.
/// </summary>
internal sealed class GuardHarness
{
    public static readonly MessageStoreKey Store = new("tests:inbox-store");

    public static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private GuardHarness(
        MessageReceivePipeline pipeline,
        InMemoryInboxDatabase database,
        TestExecutionScopeFactory scopes,
        CapturingLogger<MessageReceivePipeline> logs,
        InboxRetryPolicy policy,
        IServiceProvider provider,
        InboxBackoffGroup backoffs,
        FakeTimeProvider clock)
    {
        Backoffs = backoffs;
        Clock = clock;
        Pipeline = pipeline;
        Database = database;
        Scopes = scopes;
        Logs = logs;
        Policy = policy;
        Provider = provider;
    }

    public MessageReceivePipeline Pipeline { get; }

    public InMemoryInboxDatabase Database { get; }

    public TestExecutionScopeFactory Scopes { get; }

    public CapturingLogger<MessageReceivePipeline> Logs { get; }

    public InboxRetryPolicy Policy { get; }

    public IServiceProvider Provider { get; }

    /// <summary>Gets the inbox back-off of each store (#124, #210), shared with an inbox processor.</summary>
    public InboxBackoffGroup Backoffs { get; }

    /// <summary>Gets the clock of the retry policy and the guard.</summary>
    public FakeTimeProvider Clock { get; }

    /// <summary>Builds the harness.</summary>
    /// <param name="services">The handlers and anything else the test's scopes resolve.</param>
    /// <param name="handlers">The handler registry, filled by the test.</param>
    /// <param name="contracts">The contract registry.</param>
    /// <param name="withStore">Whether a store is configured; without one the pipeline takes the #186 path.</param>
    /// <param name="registerGuard">Whether the store registers its guard.</param>
    /// <param name="guardActivationFailure">A guard registered whose construction throws this.</param>
    /// <param name="options">The inbox options.</param>
    /// <param name="random">The jitter source; no jitter by default.</param>
    /// <param name="onGuardBuilt">Observes the scope each guard is built from.</param>
    public static GuardHarness Build(
        Action<IServiceCollection> services,
        MessageHandlerRegistry handlers,
        IntegrationEventRegistry contracts,
        bool withStore = true,
        bool registerGuard = true,
        Exception? guardActivationFailure = null,
        InboxProcessorOptions? options = null,
        Random? random = null,
        Action<IServiceProvider>? onGuardBuilt = null)
    {
        var database = new InMemoryInboxDatabase();
        var clock = new FakeTimeProvider(Now);

        var collection = new ServiceCollection();
        collection.AddScoped<ExecutionContextHolder>();
        collection.AddScoped<DeliveryStoreHolder>();
        if (guardActivationFailure is not null)
        {
            collection.AddKeyedScoped<IInboxGuardStore>(Store, (_, _) => throw guardActivationFailure);
        }
        else if (registerGuard)
        {
            collection.AddKeyedScoped<IInboxGuardStore>(Store, (sp, _) =>
            {
                onGuardBuilt?.Invoke(sp);
                return new InMemoryInboxGuard(database, clock);
            });
        }

        services(collection);
        var provider = collection.BuildServiceProvider();
        var scopes = new TestExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());

        var composition = new MessagingComposition();
        if (withStore)
        {
            composition.GetOrAddStore(Store, out _);
        }

        var inboxOptions = options ?? new InboxProcessorOptions();
        var policy = new InboxRetryPolicy(inboxOptions, clock, random ?? FixedRandom.NoJitter);
        var backoffs = new InboxBackoffGroup(inboxOptions);
        var logs = new CapturingLogger<MessageReceivePipeline>();
        var pipeline = new MessageReceivePipeline(
            contracts,
            handlers,
            new LocalConsumerRegistry(composition),
            new MicroKit.Messaging.Serialization.SystemTextJsonMessageSerializer(),
            scopes,
            new MessageStoreResolver(composition, new StaticMessageStoreSet(composition)),
            policy,
            new InboxMetrics(),
            backoffs,
            logs);

        return new GuardHarness(pipeline, database, scopes, logs, policy, provider, backoffs, clock);
    }
}

/// <summary>Records what a test handler saw, and makes it fail on demand.</summary>
internal sealed class HandlerProbe
{
    private readonly Dictionary<string, int> _calls = [];
    private readonly object _gate = new();

    /// <summary>Gets the contexts each handler received, in order.</summary>
    public List<(string Handler, MessageContext Context, MessageStoreKey? Store)> Seen { get; } = [];

    /// <summary>Gets or sets a fault the named handler throws.</summary>
    public Dictionary<string, Exception> Throw { get; } = [];

    public int Calls(string handler)
    {
        lock (_gate)
        {
            return _calls.GetValueOrDefault(handler);
        }
    }

    /// <summary>Gets or sets what every handler does before its scripted fault.</summary>
    public Action? OnHit { get; set; }

    public void Hit(string handler, MessageContext context, IServiceProvider services)
    {
        OnHit?.Invoke();
        lock (_gate)
        {
            _calls[handler] = _calls.GetValueOrDefault(handler) + 1;
            Seen.Add((handler, context, services.GetService<DeliveryStoreHolder>()?.Store));
        }

        if (Throw.TryGetValue(handler, out var fault))
        {
            throw fault;
        }
    }
}
