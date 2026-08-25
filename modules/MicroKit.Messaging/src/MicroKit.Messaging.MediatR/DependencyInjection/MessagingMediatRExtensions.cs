using MicroKit.Messaging.MediatR.Events;
using MicroKit.Messaging.MediatR.Outbox;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging.MediatR.DependencyInjection;

/// <summary>
/// DI extensions that bridge MicroKit.MediatR domain events onto the MicroKit.Messaging outbox.
/// </summary>
public static class MessagingMediatRExtensions
{
    /// <summary>
    /// Wires the MicroKit.MediatR glue onto an existing MicroKit.Messaging registration. It makes
    /// three registrations:
    /// <list type="bullet">
    ///   <item><strong>Contributes the domain-event sink.</strong>
    ///         <c>OutboxDomainEventSink</c> is added as an <c>IDomainEventsSink</c>: it maps
    ///         each drained domain event to its notification and stages them in the transactional
    ///         outbox, in the same transaction as the aggregate.</item>
    ///   <item><strong>Takes the outbox dispatcher seam.</strong>
    ///         <c>MediatROutboxDispatcher</c> becomes the <c>IOutboxDispatcher</c> the engine
    ///         resolves. It routes on <see cref="OutboxMessage.MessageKind"/>: a
    ///         <see cref="MessageKind.Notification"/> row publishes via
    ///         <see cref="IPublisher.Publish"/>, everything else is delegated to the standard
    ///         dispatcher found under <see cref="OutboxDispatcherKeys.Standard"/>.</item>
    ///   <item><strong>Replaces the notification publisher.</strong>
    ///         <c>DomainEventsCascadeNotificationPublisher</c> takes over from MediatR's
    ///         <c>ForeachAwaitPublisher</c> so domain events raised by notification handlers are
    ///         dispatched once after all handlers complete (ADR-MSG-013).</item>
    /// </list>
    /// </summary>
    /// <param name="builder">The <see cref="MessagingBuilder"/> returned by
    /// <c>AddMicroKitMessaging()</c>.</param>
    /// <returns>The same <see cref="MessagingBuilder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Call order does not matter, and it used to.</strong> The standard dispatcher lives in
    /// a keyed slot that only <c>MicroKit.Messaging</c> writes, so this method never competes for it
    /// (see <see cref="OutboxDispatcherKeys"/>). Called <em>after</em>
    /// <see cref="MessagingBuilder.AddTransportDispatcher"/>, it removes that method's unkeyed
    /// forwarder and takes the seam. Called <em>before</em> it, there is no forwarder to remove and
    /// the later <c>TryAdd</c> correctly abstains — where previously this method threw because it
    /// had nothing to wrap. The sink is contributed with <c>TryAddEnumerable</c> and is likewise
    /// order-independent with respect to <c>AddMicroKitMediatR()</c>.
    /// </para>
    /// <para>
    /// <strong>A transport is no longer a precondition.</strong> A host that publishes only
    /// domain-event notifications composes with this method alone: the keyed lookup yields
    /// <see langword="null"/>, notifications dispatch, and only a
    /// <see cref="MessageKind.Contract"/> row — which such a host does not produce — would raise
    /// <see cref="OutboxConfigurationException"/>.
    /// </para>
    /// <para>
    /// <strong>Idempotent.</strong> Calling this twice contributes one sink (<c>TryAddEnumerable</c>
    /// deduplicates on implementation type), performs one replacement, and leaves one dispatcher —
    /// the second call removes the descriptor the first added before adding its own. That is
    /// structural, replacing a marker descriptor that existed only because a factory descriptor
    /// carries no <c>ImplementationType</c> to recognise it by.
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
            ServiceDescriptor.Scoped<IDomainEventsSink, OutboxDomainEventSink>());

        TakeOutboxDispatcherSeam(builder.Services);

        // Replace MediatR's default ForeachAwaitPublisher with the cascade publisher so that
        // domain events raised by notification handlers are dispatched after every publish.
        // Registered as transient to allow the scoped IDomainEventsDispatcher to be resolved.
        // Replace is idempotent, so a second call is harmless.
        builder.Services.Replace(
            ServiceDescriptor.Transient<INotificationPublisher, DomainEventsCascadeNotificationPublisher>());

        // No IMessageSerializer TryAdd here either. MediatROutboxDispatcher needs one to publish
        // notifications, but AddMicroKitMessaging() supplies the default and this is an extension
        // on the builder it returns, so it cannot run without it (ADR-MSG-019).

        return builder;
    }

    /// <summary>
    /// Makes <c>MediatROutboxDispatcher</c> the unkeyed <see cref="IOutboxDispatcher"/>, resolving
    /// its inner from the keyed slot at activation time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The removal targets <b>unkeyed</b> descriptors only, and the predicate says so explicitly
    /// rather than relying on <c>RemoveAll&lt;T&gt;()</c>'s treatment of keyed registrations. The
    /// keyed descriptor written by <c>AddTransportDispatcher()</c> is the inner this decorator is
    /// about to resolve — removing it would disconnect the very thing being wrapped, and no
    /// compile error or startup failure would report it.
    /// </para>
    /// <para>
    /// Remove-then-add rather than <c>TryAdd</c>: <c>TryAdd</c> would abstain when the forwarder is
    /// already present, leaving notifications routed to a dispatcher that answers them with a
    /// configuration fault. It is also what makes a second call a no-op in effect — the descriptor
    /// this method added is itself unkeyed, so it is removed before its replacement is added.
    /// </para>
    /// <para>
    /// <c>GetKeyedService</c>, not <c>GetRequiredKeyedService</c>: a missing standard dispatcher is
    /// a legal composition (notification-only), and the decorator is built to tolerate a
    /// <see langword="null"/> inner. Where one <i>is</i> registered but its own dependencies are
    /// not — a transport dispatcher with no <see cref="IMessageTransport"/> — this resolution
    /// throws, inside the call <c>OutboxProcessor</c> wraps, and is converted to
    /// <see cref="OutboxConfigurationException"/> there.
    /// </para>
    /// </remarks>
    private static void TakeOutboxDispatcherSeam(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(IOutboxDispatcher) && !descriptor.IsKeyedService)
                services.RemoveAt(i);
        }

        services.Add(ServiceDescriptor.Scoped<IOutboxDispatcher>(sp => new MediatROutboxDispatcher(
            sp.GetKeyedService<IOutboxDispatcher>(OutboxDispatcherKeys.Standard),
            sp.GetRequiredService<IMessageSerializer>(),
            sp.GetRequiredService<IPublisher>(),
            sp.GetRequiredService<ILogger<MediatROutboxDispatcher>>())));
    }
}
