namespace MicroKit.Messaging.Processing;

/// <summary>
/// Drains every configured store: per store, an outbox loop, an inbox loop and their two retention
/// loops, each independent of every other.
/// </summary>
/// <remarks>
/// <para>
/// <b>Failure isolation.</b> A loop holds its own back-off and resolves its store's services per
/// cycle, keyed by the store, from a scope of its own. A store whose database is unreachable backs
/// off alone; the other stores keep their cadence.
/// </para>
/// <para>
/// The store set is read once, when the engine starts: it is static in this release.
/// </para>
/// </remarks>
internal sealed class MessageStoreEngine(
    IServiceScopeFactory scopeFactory,
    IMessageStoreSet storeSet,
    OutboxProcessorOptions outboxOptions,
    InboxProcessorOptions inboxOptions,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory)
    : BackgroundService
{
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
            loops.Add(new OutboxLoop(
                scopeFactory, store, outboxOptions, loggerFactory.CreateLogger<OutboxLoop>())
                .RunAsync(stoppingToken));
            loops.Add(new InboxLoop(
                scopeFactory, store, inboxOptions, loggerFactory.CreateLogger<InboxLoop>())
                .RunAsync(stoppingToken));
            loops.Add(new OutboxRetentionLoop(
                scopeFactory, store, outboxOptions, timeProvider,
                loggerFactory.CreateLogger<OutboxRetentionLoop>())
                .RunAsync(stoppingToken));
            loops.Add(new InboxRetentionLoop(
                scopeFactory, store, inboxOptions, timeProvider,
                loggerFactory.CreateLogger<InboxRetentionLoop>())
                .RunAsync(stoppingToken));
        }

        return Task.WhenAll(loops);
    }
}
