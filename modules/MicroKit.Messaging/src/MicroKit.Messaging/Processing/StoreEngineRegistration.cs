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
        services.TryAddSingleton<OutboxMetrics>();

        // The gate every batch of every partitioned store's instance passes (#191 D6): FIFO, bounded by
        // the options. A single store's loops never enter it.
        services.TryAddSingleton(static sp => new FifoGate(sp.GetRequiredService<StoreInstanceOptions>().MaxConcurrentBatches));

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

        // The store's endpoint back-off: a singleton, it outlives each cycle's scoped processor. Handed to
        // the outbox's instruments as it is created, so its back-off counters report the store from its
        // first cycle (#126).
        services.AddKeyedSingleton(store, static (sp, key) =>
        {
            var options = sp.GetRequiredService<OutboxProcessorOptions>();
            var backoff = new OutboxEndpointBackoff(options.PollingInterval, options.TransportUnavailableBackoff);
            sp.GetRequiredService<OutboxMetrics>().Track((MessageStoreKey)key!, backoff);
            return backoff;
        });

        // A commit wakes its store's outbox loop (#215). The wakes of the single stores are built once,
        // and read without resolving anything: a store provider reports its commit while its scope is
        // disposed. The signal is scoped: it reads the scope's holder — which instance, if any, the
        // scope serves — when it is called.
        services.TryAddSingleton(static sp => new OutboxWakes(
            sp.GetRequiredService<IMessageStoreSet>(), sp.GetRequiredService<StoreInstanceRegistry>()));
        services.TryAddScoped<IOutboxCommitSignal, OutboxCommitSignal>();

        // The processors are keyed by store and built over that store's keyed services, resolved
        // from the loop's scope: the store's instance — its DbContext included — comes from the
        // scope, never from a type fixed here. In a scope the engine prepared for an instance of a
        // partitioned store (#191), the back-off state is that instance's, named on the scope.
        services.AddKeyedScoped<IOutboxProcessor>(store, static (sp, key) =>
        {
            var instance = sp.GetService<Stores.DeliveryStoreHolder>()?.Instance;
            return new OutboxProcessor(
                sp.GetRequiredKeyedService<IOutboxProcessorStore>(key),
                sp.GetRequiredService<IExecutionScopeFactory>(),
                sp.GetRequiredService<OutboxProcessorOptions>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<Random>(),
                sp.GetRequiredService<ILogger<OutboxProcessor>>(),
                (MessageStoreKey)key!,
                instance?.OutboxBackoff ?? sp.GetRequiredKeyedService<OutboxEndpointBackoff>(key),
                sp.GetRequiredService<OutboxMetrics>(),
                instance);
        });

        services.AddKeyedScoped<IInboxProcessor>(store, static (sp, key) =>
        {
            var instance = sp.GetService<Stores.DeliveryStoreHolder>()?.Instance;
            return new InboxProcessor(
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
                instance?.InboxTenants ?? sp.GetRequiredService<InboxTenantBackoff>().For((MessageStoreKey)key!),
                instance);
        });
    }
}
