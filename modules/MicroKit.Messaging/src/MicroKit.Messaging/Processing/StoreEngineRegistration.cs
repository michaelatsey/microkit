using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The engine's registrations: once per service collection, then once per store. Called by
/// <see cref="Spi.MessageStoreRegistrationExtensions.AddMessageStore"/>, never by application code.
/// </summary>
internal static class StoreEngineRegistration
{
    /// <summary>
    /// The services the engine resolves keyed by a store's key, which host start requires of every
    /// configured store.
    /// </summary>
    internal static readonly Type[] RequiredStoreServices =
    [
        typeof(IOutboxWriter),
        typeof(IOutboxProcessorStore),
        typeof(IOutboxRetentionStore),
        typeof(IInboxGuardStore),
        typeof(IInboxProcessorStore),
        typeof(IInboxRetentionStore),
    ];

    /// <summary>Registers the engine, if not yet, and the processors of <paramref name="store"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="store">The new store's key.</param>
    public static void AddStore(IServiceCollection services, MessageStoreKey store)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MessageStoreEngine>());
        services.TryAddSingleton<InboxMetrics>();

        // Both dispatchers of a store's outbox come with the first store: a local row (#188 D7) is
        // delivered through the receive pipeline whatever the transport, and a transport row goes to
        // whatever IMessageTransport the host registers — the in-memory one or a provider's, which
        // has no other way to register this internal dispatcher. With no transport registered, the
        // transport dispatcher cannot be activated: the store's transport rows are deferred to the end
        // of the transport endpoint's back-off (event 1009, naming the call to add) until one is
        // deployed, and its local rows are delivered meanwhile.
        services.TryAddKeyedScoped<IOutboxDispatcher, Local.LocalOutboxDispatcher>(OutboxDispatchers.Local);

        // A factory, not a type: a host with a store and no transport is valid (domain events only),
        // and ValidateOnBuild — on by default in Development — would reject a typed registration
        // whose constructor needs an IMessageTransport. Resolving the factory still throws the
        // container's InvalidOperationException, so the activation verdict is unchanged.
        services.TryAddKeyedScoped<IOutboxDispatcher>(OutboxDispatchers.Transport, static (sp, _) =>
            new Dispatch.TransportOutboxDispatcher(
                sp.GetService<IMessageTransport>() ?? throw new InvalidOperationException(
                    "No IMessageTransport is registered, so a transport outbox row cannot be sent. Register " +
                    "one — bus.UseInMemoryTransport(), or a broker provider's transport — and the row is " +
                    "sent on the next attempt; the store's local rows are delivered meanwhile."),
                sp.GetRequiredService<ILogger<Dispatch.TransportOutboxDispatcher>>()));

        // The store's endpoint back-off: a singleton, it outlives each cycle's scoped processor.
        services.AddKeyedSingleton(store, static (sp, _) =>
        {
            var options = sp.GetRequiredService<OutboxProcessorOptions>();
            return new OutboxEndpointBackoff(options.PollingInterval, options.TransportUnavailableBackoff);
        });

        // The processors are keyed by store and built over that store's keyed services, resolved
        // from the loop's scope: the store's instance — its DbContext included — comes from the
        // scope, never from a type fixed here.
        services.AddKeyedScoped<IOutboxProcessor>(store, static (sp, key) => new OutboxProcessor(
            sp.GetRequiredKeyedService<IOutboxProcessorStore>(key),
            sp.GetRequiredService<IExecutionScopeFactory>(),
            sp.GetRequiredService<OutboxProcessorOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<Random>(),
            sp.GetRequiredService<ILogger<OutboxProcessor>>(),
            (MessageStoreKey)key!,
            sp.GetRequiredKeyedService<OutboxEndpointBackoff>(key)));

        services.AddKeyedScoped<IInboxProcessor>(store, static (sp, key) => new InboxProcessor(
            sp.GetRequiredKeyedService<IInboxProcessorStore>(key),
            sp.GetRequiredService<Registry.MessageHandlerRegistry>(),
            sp.GetRequiredService<Registry.LocalConsumerRegistry>(),
            sp.GetRequiredService<Publishing.IntegrationEventRegistry>(),
            sp.GetRequiredService<IMessageSerializer>(),
            sp.GetRequiredService<Bus.MessageReceivePipeline>(),
            sp.GetRequiredService<InboxProcessorOptions>(),
            sp.GetRequiredService<InboxRetryPolicy>(),
            sp.GetRequiredService<ILogger<InboxProcessor>>(),
            (MessageStoreKey)key!,
            sp.GetRequiredService<InboxTenantBackoff>().For((MessageStoreKey)key!)));
    }
}
