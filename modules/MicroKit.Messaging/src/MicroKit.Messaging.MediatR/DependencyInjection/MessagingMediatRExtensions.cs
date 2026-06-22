using MicroKit.Messaging.MediatR.Events;
using MicroKit.Messaging.MediatR.Outbox;
using MicroKit.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging.MediatR.DependencyInjection;

/// <summary>
/// DI extensions that wire MediatR as the messaging transport for <c>MicroKit.Messaging</c>.
/// </summary>
public static class MessagingMediatRExtensions
{
    /// <summary>
    /// Wires the MediatR transport bridge onto an existing MicroKit.Messaging registration:
    /// <list type="bullet">
    ///   <item><see cref="DomainEventsDispatcher"/> as <c>IDomainEventsDispatcher</c> — drains
    ///         domain events and writes their notifications to the transactional outbox.
    ///         Called by <c>TransactionBehavior</c> after the command handler completes.</item>
    ///   <item><see cref="MediatROutboxDispatcher"/> as a routing decorator over the existing
    ///         <c>IOutboxDispatcher</c> — notifications publish via <see cref="IPublisher.Publish"/>,
    ///         integration events delegate to the wrapped Core dispatcher.</item>
    /// </list>
    /// </summary>
    /// <param name="builder">The <see cref="MessagingBuilder"/> returned by
    /// <c>AddMicroKitMessaging()</c>.</param>
    /// <returns>The same <see cref="MessagingBuilder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Call order (required):</strong> call <c>AddMicroKitMediatR()</c> first (so the
    /// notification pipeline and <c>IPublisher</c> are registered), then
    /// <c>AddMicroKitMessaging(...).AddInProcessTransport()</c> (which registers the base
    /// <c>IOutboxDispatcher</c>), and finally <c>AddMediatRTransport()</c>. Calling this method
    /// before a transport has registered an <c>IOutboxDispatcher</c> throws
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// <strong>Idempotency contract:</strong> <see cref="IDomainEventHandler{TEvent}"/> handlers
    /// run synchronously in-transaction (P2). <see cref="INotificationHandler{TNotification}"/> handlers
    /// run later on the outbox processing path, after commit. Because an outbox retry re-publishes the
    /// notification and re-runs ALL notification handlers, those handlers must be idempotent
    /// (ADR-MSG-003 / ADR-MSG-009).
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddMediatRTransport(this MessagingBuilder builder)
    {
        builder.Services.AddScoped<DomainEventsDispatcher>();
        builder.Services.AddScoped<IDomainEventsDispatcher>(sp => sp.GetRequiredService<DomainEventsDispatcher>());
#pragma warning disable CS0618 // IDomainEventDispatcher is a preview compatibility alias.
        builder.Services.AddScoped<IDomainEventDispatcher>(sp => new DomainEventDispatcherCompatibilityAdapter(
            sp.GetRequiredService<IDomainEventsDispatcher>()));
#pragma warning restore CS0618

        // Capture the transport's IOutboxDispatcher and wrap it in the routing decorator.
        // The inner type (InProcessIntegrationDispatcher) is internal to Core and cannot be named
        // here, so it is rebuilt from the captured descriptor.
        var descriptor =
            builder.Services.LastOrDefault(d => d.ServiceType == typeof(IOutboxDispatcher))
            ?? throw new InvalidOperationException(
                "No IOutboxDispatcher is registered. Call AddInProcessTransport() (or a broker " +
                "transport) before AddMediatRTransport().");

        builder.Services.Remove(descriptor);

        builder.Services.Add(new ServiceDescriptor(
            typeof(IOutboxDispatcher),
            sp =>
            {
                var inner = CreateInner(sp, descriptor);
                return ActivatorUtilities.CreateInstance<MediatROutboxDispatcher>(sp, inner);
            },
            descriptor.Lifetime));

        // Replace MediatR's default ForeachAwaitPublisher with the cascade publisher so that
        // domain events raised by notification handlers are dispatched after every publish.
        // Registered as transient to allow the scoped IDomainEventsDispatcher to be resolved.
        builder.Services.Replace(ServiceDescriptor.Transient<INotificationPublisher, DomainEventsCascadeNotificationPublisher>());

        // Ensure IMessageSerializer is available even if AddInProcessTransport() was not called.
        // The in-process transport registers this too; TryAdd ensures no double registration.
        builder.Services.TryAddSingleton<IMessageSerializer, SystemTextJsonMessageSerializer>();

        return builder;
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

#pragma warning disable CS0618 // IDomainEventDispatcher is a preview compatibility alias.
file sealed class DomainEventDispatcherCompatibilityAdapter(IDomainEventsDispatcher inner) : IDomainEventDispatcher
{
    public Task DispatchEventsAsync(CancellationToken ct = default)
        => inner.DispatchEventsAsync(ct);
}
#pragma warning restore CS0618
