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
    /// Registers <c>TransportOutboxDispatcher</c>, which routes
    /// <see cref="MessageKind.Contract"/> outbox rows to an <see cref="IMessageTransport"/> as
    /// <see cref="MessageEnvelope"/>s.
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>It registers the dispatcher and nothing else — in particular, no
    /// <see cref="IMessageTransport"/>.</b> No implementation of that interface ships in any
    /// MicroKit package: an in-process transport is meaningless until the receiving seam exists,
    /// and one that returned successfully with nowhere to deliver would be the silent-success
    /// failure this module treats as blocking. A broker provider supplies the implementation, and
    /// its <c>Add{Provider}Transport()</c> extension should call this method too, so a consumer
    /// writes one line rather than two.
    /// </para>
    /// <para>
    /// <b>With no transport registered, a contract row fails loudly and reversibly.</b> The
    /// dispatcher takes <see cref="IMessageTransport"/> through its constructor, so the container
    /// fails while <c>OutboxProcessor</c> is resolving the dispatcher — which the processor
    /// converts into <see cref="OutboxConfigurationException"/>. The batch is released untouched,
    /// no retry budget is consumed, the rows stay <see cref="OutboxMessageStatus.Pending"/>, and
    /// the worker stops so the missing registration is visible rather than absorbed.
    /// </para>
    /// <para>
    /// This is deliberately <b>not</b> a startup validation, unlike the integration-event registry
    /// check. Whether a transport is needed depends on whether any contract row exists, which is
    /// data rather than composition: a host that publishes only domain-event notifications composes
    /// legitimately without a transport and must not fail at boot.
    /// </para>
    /// <para>
    /// It requires no <c>IMessageSerializer</c>, and that is not an oversight — the dispatcher
    /// carries the payload opaque and never deserializes it.
    /// </para>
    /// <para>
    /// Registered <b>scoped</b> and with <c>TryAdd</c>, for the reasons given on
    /// <see cref="AddInProcessTransport"/>: it is resolved from the per-message execution scope,
    /// and a plain <c>Add</c> would let a later registration silently displace a decorator over
    /// <see cref="IOutboxDispatcher"/>. Calling this method twice is a no-op.
    /// </para>
    /// <para>
    /// <b>The name avoids the reserved <c>Add{Provider}Transport()</c> shape on purpose.</b> That
    /// shape belongs to methods that wire an actual broker; this one wires the dispatcher that
    /// feeds whichever broker is registered.
    /// </para>
    /// </remarks>
    public MessagingBuilder AddTransportDispatcher()
    {
        Services.TryAddScoped<IOutboxDispatcher, TransportOutboxDispatcher>();
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
