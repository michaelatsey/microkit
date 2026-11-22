namespace MicroKit.Messaging.Spi;

/// <summary>
/// Prepares a scope for one instance of a partitioned store (#191), after it is created and before any
/// of its services — the store's <c>DbContext</c> first — is resolved: typically, points the scope's
/// connection at the instance's database.
/// </summary>
/// <remarks>
/// <para>
/// Part of the <b>store SPI</b>, configured with <see cref="MessageStoreBuilder.PartitionedBy{TSource, TInitializer}"/>
/// and resolved from each scope, keyed by the store. The counterpart of
/// <see cref="IMessageScopeInitializer"/> for the store: it runs in every scope the engine opens for an
/// instance — the four loops' scopes, the scope each outbox row is dispatched in, each delivery's and
/// each attempt's — and, in a message scope, before the <see cref="IMessageScopeInitializer"/>s.
/// </para>
/// <para>
/// An exception in a loop's scope is that instance's failure: its loop backs off, and no other
/// instance waits for it. Keep the initializer's own constructor dependencies free of the services
/// it prepares.
/// </para>
/// </remarks>
public interface IMessageStoreInstanceInitializer
{
    /// <summary>Prepares <paramref name="services"/> for <paramref name="instance"/>.</summary>
    /// <param name="instance">The instance the scope serves.</param>
    /// <param name="services">The scope's service provider.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the scope is ready.</returns>
    ValueTask InitializeAsync(MessageStoreInstance instance, IServiceProvider services, CancellationToken ct = default);
}
