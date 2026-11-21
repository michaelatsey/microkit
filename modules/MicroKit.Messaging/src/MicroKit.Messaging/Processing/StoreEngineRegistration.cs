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

        // A local row (#188 D7) is delivered by the store's own outbox loop, through the receive
        // pipeline: its dispatcher comes with the first store, whatever the transport.
        services.TryAddKeyedScoped<IOutboxDispatcher, Local.LocalOutboxDispatcher>(OutboxDispatchers.Local);

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
            (MessageStoreKey)key!));

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
            (MessageStoreKey)key!));
    }
}
