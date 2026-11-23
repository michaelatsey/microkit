namespace MicroKit.Messaging.Spi;

/// <summary>
/// The message stores a host configures: what publications and handlers resolve to, and what the
/// engine drains — one outbox loop, one inbox retry loop and their two retention loops per store, or
/// per instance of a partitioned store, each with its own back-off.
/// </summary>
/// <remarks>
/// <para>
/// Part of the <b>store SPI</b>. A store provider does not implement it: it registers its store
/// through <c>MessagingBuilder.AddMessageStore</c>, and the engine's own set lists every store so
/// registered. The set is static — fixed when the host starts. The services of a store are
/// resolved per scope, keyed by its <see cref="MessageStoreKey"/>, so the store instance always
/// comes from the scope and never from a type fixed at registration.
/// </para>
/// <para>
/// A store is either single — its one instance is the store itself — or partitioned (#191): the
/// engine drains the instances its source supplies at runtime, one database per tenant, say. Those
/// instances are not stores and never appear here: a partitioned store is configured, resolved and
/// bound like a single one, whether or not it has an instance yet.
/// </para>
/// <para>An empty set means the bus runs with no durability: publications go straight to the transport.</para>
/// </remarks>
public interface IMessageStoreSet
{
    /// <summary>Gets the configured stores, in registration order.</summary>
    /// <returns>The store keys; empty when no store is configured.</returns>
    IReadOnlyCollection<MessageStoreKey> GetStores();
}
