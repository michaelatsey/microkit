using System.Diagnostics.CodeAnalysis;
using MicroKit.Messaging.Dispatch;
using MicroKit.Messaging.Execution;
using MicroKit.Messaging.Registry;
using MicroKit.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    /// Registers the in-process transport: <c>SystemTextJsonMessageSerializer</c> and
    /// <c>InProcessIntegrationDispatcher</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>IMessagePublisher</c> is gone (ADR-MSG-018).</b> It handed a dispatcher's payload on
    /// as a bare event, so the fan-out behind it had to reconstruct message metadata by reading it
    /// off the event instance — the sole reason <c>IIntegrationEvent</c> carried
    /// <c>MessageId</c>, <c>TenantId</c>, <c>CorrelationId</c> and <c>CausationId</c>. The
    /// fan-out now lives in <c>InProcessIntegrationDispatcher</c>, which holds the
    /// <c>OutboxMessage</c> those fields are columns on. A real transport seam arrives with the
    /// transport libraries; this method wires the in-process path and nothing else.
    /// </para>
    /// <para>
    /// <c>IOutboxDispatcher</c> is registered as <strong>scoped</strong> — it is resolved from the
    /// per-message execution scope created by <c>OutboxProcessor</c>. Registering it as singleton
    /// would capture the scoped <c>IInboxWriter</c> (backed by a scoped <c>DbContext</c> in
    /// <c>MicroKit.Messaging.EntityFrameworkCore</c>), causing a captive dependency.
    /// </para>
    /// <para>
    /// Both are registered with <c>TryAdd</c>: a transport supplies a default and abstains if
    /// something already holds the slot. This is what makes the composition order-independent.
    /// Under a plain <c>Add</c>, calling this method <em>after</em> a package that decorates
    /// <c>IOutboxDispatcher</c> appended a second descriptor, Microsoft DI resolved the last one,
    /// and the decorator was bypassed with no exception and no log — the outbox kept draining while
    /// nothing it routed was ever published. The serializer stacked a duplicate descriptor for the
    /// same reason. Calling this method twice is now also a no-op rather than a double
    /// registration (ADR-MEDIATR-015).
    /// </para>
    /// </remarks>
    public MessagingBuilder AddInProcessTransport()
    {
        Services.TryAddSingleton<IMessageSerializer, SystemTextJsonMessageSerializer>();
        Services.TryAddScoped<IOutboxDispatcher, InProcessIntegrationDispatcher>();
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
