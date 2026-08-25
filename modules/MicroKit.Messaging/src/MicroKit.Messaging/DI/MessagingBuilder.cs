using System.Diagnostics.CodeAnalysis;
using MicroKit.Messaging.Dispatch;
using MicroKit.Messaging.Registry;
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
    /// Registers <c>TransportOutboxDispatcher</c>, which routes
    /// <see cref="MessageKind.Contract"/> outbox rows to an <see cref="IMessageTransport"/> as
    /// <see cref="MessageEnvelope"/>s.
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>It registers the dispatcher and nothing else — in particular, no
    /// <see cref="IMessageTransport"/>.</b> No implementation of that interface ships in any
    /// MicroKit package: one that returned successfully with nowhere to deliver would be the
    /// silent-success failure this module treats as blocking. A broker provider supplies the
    /// implementation, and its <c>Add{Provider}Transport()</c> extension should call this method
    /// too, so a consumer writes one line rather than two.
    /// </para>
    /// <para>
    /// <b>Two registrations, because the seam has two slots.</b> The dispatcher goes into the keyed
    /// slot named by <see cref="OutboxDispatcherKeys.Standard"/>, which only this package ever
    /// writes; a forwarder then claims the unkeyed <see cref="IOutboxDispatcher"/> slot that
    /// <c>OutboxProcessor</c> resolves. A decorating package — <c>MicroKit.Messaging.MediatR</c> is
    /// the one that ships — removes the forwarder, takes the unkeyed slot outright, and finds this
    /// dispatcher through the key.
    /// </para>
    /// <para>
    /// <b>That split is what makes the composition order-independent, and it replaces a mechanism
    /// that was not.</b> Registered <i>before</i> the glue, the forwarder is removed and the keyed
    /// dispatcher is wrapped. Registered <i>after</i> it, the keyed <c>TryAdd</c> still lands while
    /// the unkeyed one correctly abstains, leaving the decorator in place — where previously the
    /// whole method abstained and the transport dispatcher was never registered at all. Calling this
    /// method twice remains a no-op.
    /// </para>
    /// <para>
    /// <b>With no transport registered, a contract row fails loudly and reversibly.</b> The
    /// dispatcher takes <see cref="IMessageTransport"/> through its constructor, so the container
    /// fails while <c>OutboxProcessor</c> is resolving the dispatcher — which the processor converts
    /// into <see cref="OutboxConfigurationException"/>. The batch is released untouched, no retry
    /// budget is consumed, the rows stay <see cref="OutboxMessageStatus.Pending"/>, and the worker
    /// stops so the missing registration is visible rather than absorbed.
    /// </para>
    /// <para>
    /// <b>Calling this method declares an intent to send contracts, and the failure above is not
    /// scoped to them.</b> Because a decorator activates the keyed dispatcher when it is
    /// constructed, calling this without ever registering an <see cref="IMessageTransport"/> stops
    /// notification rows too. A host that publishes only domain-event notifications should
    /// therefore <b>not</b> call this method: with no transport dispatcher registered at all, the
    /// decorator's inner is simply absent, notifications dispatch, and only a contract row — which
    /// such a host does not produce — would fault.
    /// </para>
    /// <para>
    /// This is deliberately <b>not</b> a startup validation, unlike the integration-event registry
    /// check. Whether a transport is needed depends on whether any contract row exists, which is
    /// data rather than composition.
    /// </para>
    /// <para>
    /// It requires no <c>IMessageSerializer</c>, and that is not an oversight — the dispatcher
    /// carries the payload opaque and never deserializes it.
    /// </para>
    /// <para>
    /// <b>The name avoids the reserved <c>Add{Provider}Transport()</c> shape on purpose.</b> That
    /// shape belongs to methods that wire an actual broker; this one wires the dispatcher that
    /// feeds whichever broker is registered.
    /// </para>
    /// </remarks>
    public MessagingBuilder AddTransportDispatcher()
    {
        // The real registration. Keyed, so a decorator can find it without competing for the slot
        // the engine resolves.
        Services.TryAddKeyedScoped<IOutboxDispatcher, TransportOutboxDispatcher>(
            OutboxDispatcherKeys.Standard);

        // The unkeyed seam OutboxProcessor resolves. TryAdd, so a decorator already holding it is
        // not displaced — which is the direction that had shipped as a silent bypass (ADR-MSG-016).
        Services.TryAddScoped<IOutboxDispatcher>(
            sp => sp.GetRequiredKeyedService<IOutboxDispatcher>(OutboxDispatcherKeys.Standard));

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
    /// <para>
    /// ⚠ <b>Nothing feeds these handlers in this release, and calling this method therefore fails at
    /// startup.</b> In-process inbox ingestion was withdrawn together with the in-process fan-out
    /// (ADR-MSG-019): the outbox now hands a <see cref="MessageKind.Contract"/> row to a transport
    /// as a <see cref="MessageEnvelope"/>, and the receiving seam that turns an envelope back into
    /// inbox rows has not shipped yet. Until it does, a registered handler is unreachable — rows
    /// never appear, <c>InboxProcessor</c> never claims one, and nothing downstream can detect the
    /// shortfall. <c>InboxIngestionValidator</c> converts that silence into a boot failure rather
    /// than letting a host run believing it is consuming events.
    /// </para>
    /// <para>
    /// The registry, <c>InboxProcessor</c> and the whole drain pipeline are unchanged and still
    /// correct — they have no producer, which is not the same as being broken. A host that writes
    /// inbox rows itself can still drive them.
    /// </para>
    /// <para>
    /// Handlers are registered as <strong>transient</strong>. They are resolved from the
    /// per-message scope created by <c>InboxProcessor</c> — each message invocation gets
    /// a fresh handler instance. The registry is populated before the
    /// <see cref="IServiceProvider"/> is built.
    /// </para>
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
