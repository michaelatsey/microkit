using System.Collections.Frozen;

namespace MicroKit.Messaging.Registry;

/// <summary>
/// The local consumers (#188 D7), read once from every <see cref="ILocalConsumerSource"/> added to
/// the composition, indexed like <see cref="MessageHandlerRegistry"/>.
/// </summary>
/// <remarks>
/// A singleton built on first resolution — after the container is built — so a source sees every
/// registration the host made, whatever the order of the calls; its indexes are frozen then, since
/// every local delivery reads them. A consumer named twice for one type is kept once, the later
/// replacing the earlier, as handler registration does.
/// </remarks>
internal sealed class LocalConsumerRegistry
{
    private readonly FrozenDictionary<Type, MessageHandlerRegistry.HandlerEntry[]> _byType;
    private readonly FrozenDictionary<(string ConsumerType, Type MessageType), MessageHandlerRegistry.HandlerEntry> _byConsumer;

    /// <summary>Reads every source of the composition.</summary>
    /// <param name="composition">The composition holding the sources.</param>
    public LocalConsumerRegistry(MessagingComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        // Insertion order of first registration, a later duplicate replacing its entry in place.
        Dictionary<(string ConsumerType, Type MessageType), LocalConsumer> consumers = [];
        List<(string ConsumerType, Type MessageType)> order = [];
        foreach (var source in composition.LocalConsumerSources)
        {
            foreach (var consumer in source.GetConsumers())
            {
                ArgumentNullException.ThrowIfNull(consumer);
                var key = (consumer.ConsumerType, consumer.MessageType);
                if (!consumers.ContainsKey(key))
                {
                    order.Add(key);
                }

                consumers[key] = consumer;
            }
        }

        Consumers = [.. order.Select(k => consumers[k])];
        _byConsumer = Consumers.ToFrozenDictionary(
            static c => (c.ConsumerType, c.MessageType), MessageHandlerRegistry.HandlerEntry.Of);
        _byType = Consumers
            .GroupBy(static c => c.MessageType)
            .ToFrozenDictionary(static g => g.Key, static g => g.Select(MessageHandlerRegistry.HandlerEntry.Of).ToArray());
    }

    /// <summary>Gets every consumer, each once.</summary>
    public IReadOnlyList<LocalConsumer> Consumers { get; }

    /// <summary>The consumers of <paramref name="messageType"/>, matched exactly.</summary>
    /// <param name="messageType">The local message's runtime type.</param>
    /// <returns>Its consumers; empty when it has none.</returns>
    public IReadOnlyList<MessageHandlerRegistry.HandlerEntry> GetConsumers(Type messageType)
        => _byType.TryGetValue(messageType, out var entries) ? entries : [];

    /// <summary>The consumer named <paramref name="consumerType"/> for <paramref name="messageType"/>.</summary>
    /// <param name="consumerType">An inbox row's consumer type.</param>
    /// <param name="messageType">The row's message type.</param>
    /// <param name="entry">The consumer, when registered.</param>
    /// <returns><see langword="true"/> when the pair is registered.</returns>
    public bool TryGetConsumer(string consumerType, Type messageType, out MessageHandlerRegistry.HandlerEntry entry)
        => _byConsumer.TryGetValue((consumerType, messageType), out entry);
}
