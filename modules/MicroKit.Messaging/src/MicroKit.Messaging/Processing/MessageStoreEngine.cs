namespace MicroKit.Messaging.Processing;

/// <summary>
/// Drains every configured store: per single store, an outbox loop, an inbox loop and their two
/// retention loops, each independent of every other; per partitioned store (#191), a reconciler that
/// runs the same four loops for each instance its source supplies.
/// </summary>
/// <remarks>
/// <para>
/// <b>Failure isolation.</b> A loop holds its own back-off and resolves its store's services per
/// cycle, keyed by the store, from a scope of its own. A store — or an instance — whose database is
/// unreachable backs off alone; the others keep their cadence.
/// </para>
/// <para>
/// The configured stores are read once, when the engine starts: what a host configures is fixed at
/// start. A partitioned store's instances change at runtime, through its source
/// (<see cref="StoreInstanceReconciler"/>).
/// </para>
/// </remarks>
internal sealed class MessageStoreEngine(
    IServiceScopeFactory scopeFactory,
    IMessageStoreSet storeSet,
    OutboxProcessorOptions outboxOptions,
    InboxProcessorOptions inboxOptions,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory,
    StoreInstanceRegistry? instances = null,
    InboxBackoffGroup? inboxBackoff = null,
    OutboxMetrics? outboxMetrics = null,
    StoreInstanceOptions? instanceOptions = null,
    FifoGate? gate = null,
    InboxMetrics? inboxMetrics = null,
    OutboxWakes? outboxWakes = null)
    : BackgroundService
{
    /// <summary>
    /// The inbox's instruments, built with the engine rather than with the first delivery: a partitioned
    /// store reports from the start, at zero while it has no instance (#191 D9).
    /// </summary>
    private readonly InboxMetrics? _inboxMetrics = inboxMetrics;

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var stores = storeSet.GetStores();
        StoreEngineLogs.EngineStarted(
            loggerFactory.CreateLogger<MessageStoreEngine>(),
            stores.Count,
            string.Join(", ", stores.Select(s => $"'{s}'")));

        var loops = new List<Task>(stores.Count * 4);
        foreach (var store in stores)
        {
            if (instances is not null && instances.IsPartitioned(store))
            {
                loops.Add(new StoreInstanceReconciler(
                    store,
                    instances,
                    inboxBackoff ?? throw Missing(nameof(InboxBackoffGroup)),
                    outboxMetrics ?? throw Missing(nameof(OutboxMetrics)),
                    outboxOptions,
                    instanceOptions ?? throw Missing(nameof(StoreInstanceOptions)),
                    gate ?? throw Missing(nameof(FifoGate)),
                    (instance, ct) => RunLoopsAsync(instance.Store, instance, ct),
                    timeProvider,
                    loggerFactory.CreateLogger<StoreInstanceReconciler>())
                    .RunAsync(stoppingToken));
                continue;
            }

            loops.Add(RunLoopsAsync(store, instance: null, stoppingToken));
        }

        return Task.WhenAll(loops);
    }

    /// <summary>
    /// Runs the four loops of a single store, or of one instance, until they end. Only the outbox loop
    /// has a wake (#215): a single store's from <see cref="OutboxWakes"/>, an instance's its own.
    /// </summary>
    private Task RunLoopsAsync(MessageStoreKey store, StoreInstanceState? instance, CancellationToken stoppingToken)
        => Task.WhenAll(
            new OutboxLoop(
                scopeFactory, store, outboxOptions, loggerFactory.CreateLogger<OutboxLoop>(), instance,
                instance?.OutboxWake ?? outboxWakes?.Of(store))
                .RunAsync(stoppingToken),
            new InboxLoop(
                scopeFactory, store, inboxOptions, loggerFactory.CreateLogger<InboxLoop>(), instance)
                .RunAsync(stoppingToken),
            new OutboxRetentionLoop(
                scopeFactory, store, outboxOptions, timeProvider,
                loggerFactory.CreateLogger<OutboxRetentionLoop>(), instance)
                .RunAsync(stoppingToken),
            new InboxRetentionLoop(
                scopeFactory, store, inboxOptions, timeProvider,
                loggerFactory.CreateLogger<InboxRetentionLoop>(), instance)
                .RunAsync(stoppingToken));

    private static InvalidOperationException Missing(string service)
        => new($"The engine drains a partitioned store but was built without {service}: register it through AddMessageStore.");
}
