namespace MicroKit.Messaging.Local;

using MicroKit.Messaging.Outbox;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Stores;

/// <summary>
/// The standard <see cref="ILocalMessageSender"/>: with a store, enlists a local outbox row in the
/// store a publication of the message's type would use; without one, queues it in memory (#188 D7).
/// </summary>
/// <remarks>
/// The store is chosen by <see cref="MessageStoreResolver"/> exactly as for a publication — the
/// delivery's store, the only store, the store bound to the message type's assembly — so a local
/// message raised while a handler runs commits with that handler's work. Tenant, correlation and
/// causation come from the scope's <see cref="IExecutionContext"/>.
/// </remarks>
internal sealed class LocalMessageSender(
    MessageStoreResolver stores,
    DeliveryStoreHolder deliveryStore,
    Registry.LocalConsumerRegistry consumers,
    Processing.InboxMetrics metrics,
    OutboxMessageFactory factory,
    IntegrationEventRegistry registry,
    IExecutionContext executionContext,
    LocalQueue queue,
    IServiceProvider services,
    ILogger<LocalMessageSender> logger)
    : ILocalMessageSender
{
    /// <inheritdoc />
    public async ValueTask SendAsync(
        object message, MessageId messageId, DateTimeOffset occurredOnUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageId);

        var store = stores.ForPublication(message.GetType(), explicitStore: null, deliveryStore.Store);
        var row = factory.CreateLocal(message, messageId, occurredOnUtc, registry.Source, executionContext);

        if (store is null)
        {
            // Nothing to deliver it to: the pipeline's verdict for a message no handler consumes —
            // acknowledged, counted, and never silent — rather than a queue nobody drains.
            var targets = consumers.GetConsumers(message.GetType());
            if (targets.Count == 0)
            {
                var type = message.GetType().FullName!;
                Bus.BusLogs.NoHandler(logger, messageId.ToString(), type, row.Source ?? string.Empty, type);
                metrics.RecordUnconsumed(type);
                return;
            }

            // One entry per consumer, sharing the row: each is retried alone, as a store delivers
            // once per consumer under its own inbox row (D8).
            for (var i = 0; i < targets.Count; i++)
            {
                queue.Enqueue(row, targets[i].ConsumerType);
            }

            LocalLogs.Queued(logger, messageId.Value, message.GetType().FullName!);
            return;
        }

        await services.GetRequiredKeyedService<IOutboxWriter>(store).AddAsync(row, ct).ConfigureAwait(false);
        LocalLogs.Enlisted(logger, messageId.Value, message.GetType().FullName!, store.Value);
    }
}
