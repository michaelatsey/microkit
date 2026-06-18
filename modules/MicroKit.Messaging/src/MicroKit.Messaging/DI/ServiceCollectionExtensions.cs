using MicroKit.Messaging.Execution;
using MicroKit.Messaging.Processing;
using MicroKit.Messaging.Registry;

namespace MicroKit.Messaging;

/// <summary>
/// Extension methods for registering MicroKit.Messaging core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers MicroKit.Messaging core services: outbox and inbox workers, the
    /// in-process execution scope factory, and the handler registry.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configureOutbox">Optional callback to configure outbox processor options.</param>
    /// <param name="configureInbox">Optional callback to configure inbox processor options.</param>
    /// <returns>
    /// A <see cref="MessagingBuilder"/> for chaining additional registrations
    /// (transports and handlers).
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Required services NOT registered here:</strong>
    /// <list type="bullet">
    /// <item><description>
    /// <c>IOutboxProcessorStore</c> — must be provided by a store implementation,
    /// e.g. <c>AddEfCoreOutbox()</c> from <c>MicroKit.Messaging.EntityFrameworkCore</c>.
    /// </description></item>
    /// <item><description>
    /// <c>IInboxStore</c> — same requirement as above.
    /// </description></item>
    /// <item><description>
    /// <c>IMessageSerializer</c>, <c>IMessagePublisher</c>, <c>IOutboxDispatcher</c> — call
    /// <see cref="MessagingBuilder.AddInProcessTransport"/> on the returned builder.
    /// </description></item>
    /// </list>
    /// The outbox and inbox workers will log a critical error and stop if required services
    /// are missing at runtime.
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddMicroKitMessaging(
        this IServiceCollection services,
        Action<OutboxProcessorOptions>? configureOutbox = null,
        Action<InboxProcessorOptions>? configureInbox = null)
    {
        var outboxOptions = new OutboxProcessorOptions();
        configureOutbox?.Invoke(outboxOptions);

        var inboxOptions = new InboxProcessorOptions();
        configureInbox?.Invoke(inboxOptions);

        var registry = new MessageHandlerRegistry();

        services.AddSingleton(registry);
        services.AddSingleton(outboxOptions);
        services.AddSingleton(inboxOptions);
        services.AddSingleton<IExecutionScopeFactory, PassThroughExecutionScopeFactory>();

        services.AddScoped<IOutboxProcessor, OutboxProcessor>();
        services.AddScoped<IOutboxCoordinator, SharedDbOutboxCoordinator>();
        services.AddHostedService<OutboxWorker>();

        services.AddScoped<IInboxProcessor, InboxProcessor>();
        services.AddScoped<IInboxCoordinator, SharedDbInboxCoordinator>();
        services.AddHostedService<InboxWorker>();

        return new MessagingBuilder(services, registry);
    }
}
