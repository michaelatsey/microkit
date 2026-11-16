using MicroKit.Messaging.Processing;

namespace MicroKit.Messaging.Spi;

/// <summary>The registration path any store provider uses to add a message store to the bus.</summary>
/// <remarks>
/// Part of the <b>store SPI</b>: application code configures a store through its provider — for
/// EF Core, <c>bus.UseEntityFrameworkOutbox&lt;TContext&gt;()</c> — which calls this method.
/// </remarks>
public static class MessageStoreRegistrationExtensions
{
    /// <summary>
    /// Adds the store named <paramref name="key"/>, or reaches it again, and switches on the engine
    /// that drains every store.
    /// </summary>
    /// <param name="builder">The messaging builder.</param>
    /// <param name="key">The store's key, chosen by the provider.</param>
    /// <param name="configure">Registers the store's services, keyed by <paramref name="key"/>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// With at least one store configured, a publication is enlisted in a store's outbox rather
    /// than sent: the store is chosen per publication from the scope (see
    /// <see cref="PublishOptions.Store"/>). The engine — one outbox loop, one inbox loop and their
    /// two retention loops per store — is registered once, whatever the number of stores.
    /// </para>
    /// <para>
    /// The services the engine resolves keyed by <paramref name="key"/> are validated at host
    /// start: <see cref="IOutboxWriter"/>, <see cref="IOutboxProcessorStore"/>,
    /// <see cref="IOutboxRetentionStore"/>, <see cref="IInboxWriter"/>,
    /// <see cref="IInboxProcessorStore"/>, <see cref="IInboxSettlementStore"/> and
    /// <see cref="IInboxRetentionStore"/>.
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddMessageStore(
        this MessagingBuilder builder, MessageStoreKey key, Action<MessageStoreBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(configure);

        var registration = builder.Composition.GetOrAddStore(key, out var created);
        if (created)
        {
            StoreEngineRegistration.AddStore(builder.Services, key);
        }

        configure(new MessageStoreBuilder(builder.Services, registration));
        return builder;
    }
}
