using MicroKit.Messaging.MediatR.Events;
using MicroKit.Messaging.MediatR.Outbox;
using MicroKit.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging.MediatR.DependencyInjection;

/// <summary>
/// DI extensions that bridge MicroKit.MediatR domain events onto the MicroKit.Messaging outbox.
/// </summary>
public static class MessagingMediatRExtensions
{
    /// <summary>
    /// Wires the MicroKit.MediatR glue onto an existing MicroKit.Messaging registration. It does
    /// three things:
    /// <list type="bullet">
    ///   <item><strong>Contributes the domain-event sink.</strong>
    ///         <see cref="OutboxDomainEventSink"/> is added as an <c>IDomainEventSink</c>: it maps
    ///         each drained domain event to its notification and stages them in the transactional
    ///         outbox, in the same transaction as the aggregate.</item>
    ///   <item><strong>Decorates the outbox dispatcher.</strong>
    ///         <see cref="MediatROutboxDispatcher"/> wraps the transport's <c>IOutboxDispatcher</c>
    ///         and routes by payload: notifications publish via <see cref="IPublisher.Publish"/>,
    ///         integration events delegate to the wrapped dispatcher.</item>
    ///   <item><strong>Replaces the notification publisher.</strong>
    ///         <see cref="DomainEventsCascadeNotificationPublisher"/> takes over from MediatR's
    ///         <c>ForeachAwaitPublisher</c> so domain events raised by notification handlers are
    ///         dispatched once after all handlers complete (ADR-MSG-013).</item>
    /// </list>
    /// </summary>
    /// <param name="builder">The <see cref="MessagingBuilder"/> returned by
    /// <c>AddMicroKitMessaging()</c>.</param>
    /// <returns>The same <see cref="MessagingBuilder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// No <c>IOutboxDispatcher</c> is registered yet, so there is nothing to decorate. Call
    /// <see cref="MessagingBuilder.AddInProcessTransport"/> (or a broker transport) first.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <strong>Call order.</strong> A transport must be registered before this method, because the
    /// outbox-dispatcher decorator needs something to wrap; calling it first throws rather than
    /// failing silently. Everything else is order-independent: the sink is contributed with
    /// <c>TryAddEnumerable</c> and is resolved alongside any other, so it does not matter whether
    /// <c>AddMicroKitMediatR()</c> ran before or after; and registering a transport <em>after</em>
    /// this method no longer displaces the decorator. This method is idempotent — calling it twice
    /// contributes one sink and applies one decorator.
    /// </para>
    /// <para>
    /// <strong>Idempotency contract:</strong> <see cref="IDomainEventHandler{TEvent}"/> handlers
    /// run synchronously in-transaction. <see cref="INotificationHandler{TNotification}"/> handlers
    /// run later on the outbox processing path, after commit. Because an outbox retry re-publishes
    /// the notification and re-runs ALL notification handlers, those handlers must be idempotent
    /// (ADR-MSG-003 / ADR-MSG-009).
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddMediatRDomainEvents(this MessagingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Contribute the outbox sink to the core orchestrator (ADR-MEDIATR-014). This package
        // registers NO IDomainEventsDispatcher: there is one implementation, in MicroKit.MediatR,
        // and higher-level packages extend it by contributing sinks.
        //
        // TryAddEnumerable, not Add: it deduplicates on (ServiceType, ImplementationType), so a
        // second call to this method does not contribute a second sink that would write twice.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IDomainEventSink, OutboxDomainEventSink>());

        DecorateOutboxDispatcherOnce(builder.Services);

        // Replace MediatR's default ForeachAwaitPublisher with the cascade publisher so that
        // domain events raised by notification handlers are dispatched after every publish.
        // Registered as transient to allow the scoped IDomainEventsDispatcher to be resolved.
        // Replace is idempotent, so a second call is harmless.
        builder.Services.Replace(
            ServiceDescriptor.Transient<INotificationPublisher, DomainEventsCascadeNotificationPublisher>());

        // Ensure IMessageSerializer is available even if AddInProcessTransport() was not called —
        // a broker transport may register IOutboxDispatcher without one.
        builder.Services.TryAddSingleton<IMessageSerializer, SystemTextJsonMessageSerializer>();

        return builder;
    }

    private static void DecorateOutboxDispatcherOnce(IServiceCollection services)
    {
        // The decorator is registered as a factory, so it carries no ImplementationType and cannot
        // be recognised by inspecting the IOutboxDispatcher descriptor. A marker descriptor records
        // that the decoration has been applied; without it a second call would find its OWN factory
        // descriptor, remove it, and wrap it again — double-routing every outbox message.
        if (services.Any(d => d.ServiceType == typeof(OutboxDispatcherDecorationMarker)))
            return;

        // Capture the transport's IOutboxDispatcher and wrap it in the routing decorator.
        // The inner type (InProcessIntegrationDispatcher) is internal to Core and cannot be named
        // here, so it is rebuilt from the captured descriptor.
        var descriptor =
            services.LastOrDefault(d => d.ServiceType == typeof(IOutboxDispatcher))
            ?? throw new InvalidOperationException(
                "No IOutboxDispatcher is registered. Call AddInProcessTransport() (or a broker " +
                "transport) before AddMediatRDomainEvents().");

        services.Remove(descriptor);

        services.Add(new ServiceDescriptor(
            typeof(IOutboxDispatcher),
            sp =>
            {
                var inner = CreateInner(sp, descriptor);
                return ActivatorUtilities.CreateInstance<MediatROutboxDispatcher>(sp, inner);
            },
            descriptor.Lifetime));

        services.Add(ServiceDescriptor.Singleton(new OutboxDispatcherDecorationMarker()));
    }

    private static IOutboxDispatcher CreateInner(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IOutboxDispatcher instance)
            return instance;

        if (descriptor.ImplementationFactory is not null)
            return (IOutboxDispatcher)descriptor.ImplementationFactory(sp);

        return (IOutboxDispatcher)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
    }
}

/// <summary>
/// Registration-only marker recording that the <c>IOutboxDispatcher</c> decoration has been
/// applied. Never resolved — only its presence in the service collection is read.
/// </summary>
internal sealed class OutboxDispatcherDecorationMarker;
