using MicroKit.Messaging.MediatR.Events;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging.MediatR.DependencyInjection;

/// <summary>
/// Puts MicroKit.MediatR domain events on the bus: each notification becomes a local message,
/// delivered to each of its handlers under that handler's own inbox row (#188 D8).
/// </summary>
public static class MessagingMediatRExtensions
{
    /// <summary>
    /// Sends the notification of each domain event as a local message, delivers it to each
    /// <see cref="INotificationHandler{TNotification}"/> as a consumer of its own, and dispatches the
    /// domain events a handler raises before its work is saved.
    /// </summary>
    /// <param name="bus">The builder of <c>AddMicroKitMessaging</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Three registrations, on the bus's public SPI.</b>
    /// <list type="bullet">
    ///   <item>An <see cref="IDomainEventsSink"/>: when MicroKit.MediatR dispatches the domain events
    ///         of a unit of work, each event's notification is sent through
    ///         <see cref="ILocalMessageSender"/> — enlisted in a store's outbox, or, with no store,
    ///         queued in memory. Never handed to a transport; no
    ///         <see cref="MessageContractAttribute"/> is needed.</item>
    ///   <item>A local consumer per <see cref="INotificationHandler{TNotification}"/> of a
    ///         domain-event notification, delivered on its own: one handler failing is retried alone
    ///         while the others are done — from its inbox row with a store, from its in-memory entry
    ///         without one. A handler never runs in the scope that raised the event.</item>
    ///   <item>A receive middleware that calls <see cref="IDomainEventsDispatcher.DispatchEventsAsync"/>
    ///         after each handler — a bus handler's or a notification handler's — and before the
    ///         engine saves its work, so the events it raised (cascade events) commit with that work
    ///         and are delivered in turn.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Checked at host start</b>, with the bus's own composition: MicroKit.MediatR's dispatcher
    /// must be registered (<c>AddMicroKitMediatR()</c>), and every notification handler must be one
    /// closed, concrete handler registered by its type — an open generic, a handler of an abstract
    /// notification and a factory or instance registration are each reported, never skipped.
    /// </para>
    /// <para>
    /// <b>With a store</b> a notification commits with the work that raised it and each handler's
    /// database effects through the store's context commit exactly once. With several stores, a
    /// notification is enlisted in the store bound to its type's assembly, whose context the unit of
    /// work must save. <b>Without a store</b> a notification is queued in memory as it is raised,
    /// before that work commits: it is delivered even if that work then rolls back, and lost if the
    /// process stops. Durability needs a store.
    /// </para>
    /// <para>Idempotent. The order with <c>AddMicroKitMediatR()</c> does not matter.</para>
    /// </remarks>
    public static MessagingBuilder UseMediatRDomainEvents(this MessagingBuilder bus)
    {
        ArgumentNullException.ThrowIfNull(bus);

        if (bus.Services.Any(static d => d.ServiceType == typeof(NotificationHandlerSource)))
        {
            return bus;
        }

        var source = new NotificationHandlerSource(bus.Services);
        bus.Services.AddSingleton(source);

        // TryAddEnumerable: the orchestrator resolves every sink, so a second one would send twice.
        bus.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IDomainEventsSink, DomainEventNotificationSink>());
        bus.AddReceiveMiddleware<DomainEventsDispatchMiddleware>();
        bus.AddLocalConsumerSource(source);
        bus.AddStartupCheck(services => [.. MissingMediatR(services), .. source.Problems()]);

        return bus;
    }

    private static IEnumerable<string> MissingMediatR(IServiceProvider services)
    {
        var registered = services.GetService<IServiceProviderIsService>();
        if (registered is null)
        {
            yield break;
        }

        if (!registered.IsService(typeof(IDomainEventsDispatcher)))
        {
            yield return
                "UseMediatRDomainEvents() was called, but MicroKit.MediatR's IDomainEventsDispatcher is " +
                "not registered, so no domain event is ever dispatched to it. Call AddMicroKitMediatR().";
        }

        if (!registered.IsService(typeof(IDomainEventNotificationFactory)))
        {
            yield return
                "UseMediatRDomainEvents() was called, but MicroKit.MediatR's " +
                "IDomainEventNotificationFactory is not registered, so no domain event can be mapped to " +
                "its notification. Call AddMicroKitMediatR().";
        }
    }
}
