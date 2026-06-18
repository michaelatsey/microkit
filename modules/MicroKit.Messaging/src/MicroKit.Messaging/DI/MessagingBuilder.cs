using System.Diagnostics.CodeAnalysis;
using MicroKit.Messaging.Dispatch;
using MicroKit.Messaging.Execution;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Registry;
using MicroKit.Messaging.Serialization;

namespace MicroKit.Messaging;

/// <summary>
/// Fluent builder returned by <see cref="ServiceCollectionExtensions.AddMicroKitMessaging"/>.
/// Used to register transports and message handlers after the core services are wired.
/// </summary>
public sealed class MessagingBuilder
{
    private readonly MessageHandlerRegistry _registry;

    internal MessagingBuilder(IServiceCollection services, MessageHandlerRegistry registry)
    {
        Services = services;
        _registry = registry;
    }

    /// <summary>
    /// Gets the underlying service collection for advanced registrations.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers the in-process transport: <c>SystemTextJsonMessageSerializer</c>,
    /// <c>InProcessMessagePublisher</c>, and <c>InProcessIntegrationDispatcher</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>IMessagePublisher</c> and <c>IOutboxDispatcher</c> are registered as
    /// <strong>scoped</strong> — they are resolved from the per-message execution scope
    /// created by <c>OutboxProcessor</c>. Registering them as singleton would capture
    /// the scoped <c>IInboxStore</c> (backed by a scoped <c>DbContext</c> in
    /// <c>MicroKit.Messaging.EntityFrameworkCore</c>), causing a captive dependency.
    /// </para>
    /// </remarks>
    public MessagingBuilder AddInProcessTransport()
    {
        Services.AddSingleton<IMessageSerializer, SystemTextJsonMessageSerializer>();
        Services.AddScoped<IMessagePublisher, InProcessMessagePublisher>();
        Services.AddScoped<IOutboxDispatcher, InProcessIntegrationDispatcher>();
        return this;
    }

    /// <summary>
    /// Registers a message handler and its event type association in the handler registry.
    /// </summary>
    /// <typeparam name="THandler">
    /// The handler implementation type. Must implement <see cref="IMessageHandler{TEvent}"/>.
    /// </typeparam>
    /// <typeparam name="TEvent">
    /// The integration event type handled by <typeparamref name="THandler"/>.
    /// </typeparam>
    /// <remarks>
    /// Handlers are registered as <strong>transient</strong>. They are resolved from the
    /// per-message scope created by <c>InboxProcessor</c> — each message invocation gets
    /// a fresh handler instance. The registry is populated before the
    /// <see cref="IServiceProvider"/> is built.
    /// </remarks>
    public MessagingBuilder AddMessageHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TEvent>()
        where THandler : class, IMessageHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        Services.AddTransient<THandler>();

        _registry.RegisterGeneric<TEvent>(
            typeof(THandler).AssemblyQualifiedName!,
            typeof(THandler));

        return this;
    }
}
