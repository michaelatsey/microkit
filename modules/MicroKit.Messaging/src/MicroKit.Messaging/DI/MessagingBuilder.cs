using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MicroKit.Messaging.Dispatch;
using MicroKit.Messaging.Registry;
using MicroKit.Messaging.Serialization;
using MicroKit.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging;

/// <summary>
/// Fluent builder returned by <see cref="ServiceCollectionExtensions.AddMicroKitMessaging"/>.
/// Used to register transports, message handlers, middleware, scope initializers and serializer
/// options after the core services are wired.
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
    /// <see cref="IMessageTransport"/>.</b> The only implementation shipped is the in-memory one,
    /// registered by <see cref="UseInMemoryTransport"/> together with its consume loop. A broker
    /// provider supplies its own, and its <c>Add{Provider}Transport()</c> extension should call
    /// this method too, so a consumer writes one line rather than two.
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
    /// throws <see cref="InvalidOperationException"/> while <c>OutboxProcessor</c> is activating the
    /// dispatcher. That is a registered dispatcher that cannot be activated, not a missing
    /// registration, and it is not <see cref="OutboxConfigurationException"/>: the batch is released
    /// untouched, no retry budget is consumed, the rows stay <see cref="OutboxMessageStatus.Pending"/>,
    /// the batch result reports <c>OutboxBatchAbortReason.DispatcherActivationFailed</c>, and an
    /// error is logged on every cycle. The worker keeps running and backs off rather than stopping, so
    /// the rows drain on the first cycle after a build that registers a transport is deployed, and a
    /// cause that clears on its own drains them with no intervention at all.
    /// </para>
    /// <para>
    /// <b>Only that exception type gets that verdict.</b> A constructor in the dispatcher's graph that
    /// throws any other type — a broker client failing to connect with its own exception, say — is
    /// classified as if the send had thrown it: <see cref="OutboxTransportUnavailableException"/>
    /// releases the batch, and anything untyped is retried per message. A provider whose client knows
    /// the broker is down should raise the former.
    /// </para>
    /// <para>
    /// ⚠ <b>Known defect — a failure scoped to one tenant stalls every tenant.</b> The dispatcher is
    /// activated in the scope of the row being dispatched, built from its tenant. If anything in this
    /// graph is resolved per tenant — a transport holding per-tenant credentials, say — and throws
    /// <see cref="InvalidOperationException"/> for a tenant that never recovers, that row heads every
    /// claim and no tenant publishes, with no dead-letter exit. ADR-MSG-019 records it and the fix
    /// owed; the module README gives the way out.
    /// </para>
    /// <para>
    /// <b>Calling this method declares an intent to send contracts, and the failure above is not
    /// scoped to them.</b> Because a decorator activates the keyed dispatcher when it is
    /// constructed, calling this without ever registering an <see cref="IMessageTransport"/> blocks
    /// notification rows too. A host that publishes only domain-event notifications should
    /// therefore <b>not</b> call this method: with no transport dispatcher registered at all, the
    /// decorator's inner is simply absent, notifications dispatch, and only a contract row — which
    /// such a host does not produce — would fault.
    /// </para>
    /// <para>
    /// This is deliberately <b>not</b> a startup validation, unlike the integration-event registry
    /// check. Whether a transport is needed depends on whether any contract row exists, which is
    /// data rather than composition. A service provider built with <c>ValidateOnBuild</c> — which the
    /// default host builders turn on in the <c>Development</c> environment — is the exception: the
    /// container checks the keyed dispatcher's constructor when it is built, so there a missing
    /// transport fails at startup instead.
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
    /// <b>On the bus</b> (#186), a handler is reached once the contract binds to
    /// <typeparamref name="TEvent"/> — through <c>AddIntegrationEventContracts</c> with
    /// <c>Publishes&lt;TEvent&gt;()</c> or <c>AddIntegrationEventSubscriptions</c> with
    /// <c>Consumes&lt;TEvent&gt;()</c> — and a transport delivers: <see cref="UseInMemoryTransport"/>
    /// in this package. The handler runs inline on receipt, in its own message scope. Discovery
    /// and a single registration call are #187.
    /// </para>
    /// <para>
    /// <b>On the transitional inbox path</b>, rows arrive through <see cref="IEnvelopeReceiver"/>,
    /// which also needs <c>AddEfCoreOutbox&lt;TContext&gt;()</c> so there is somewhere to write.
    /// Miss the binding and the receiver refuses the message permanently; miss this call and it
    /// logs at <c>Warning</c> and writes nothing.
    /// </para>
    /// <para>
    /// <b>Calling it twice for the same <typeparamref name="THandler"/> registers one consumer,
    /// not two.</b> The registry deduplicates on the consumer type, so the second call replaces
    /// the first entry rather than appending. Worth relying on: a composition root assembled from
    /// several module registrations doubles a handler easily, and without the dedup the receiver
    /// would write two inbox rows per envelope for that consumer — the second absorbed by the
    /// dedup index, so a <i>first</i> delivery would show up as a redelivery on
    /// <c>microkit.inbox.messages.deduplicated</c> and a composition typo would read as a broker
    /// fault. <c>AddTransient&lt;THandler&gt;()</c> underneath is not deduplicated, but a second
    /// transient descriptor for one type changes nothing about resolution.
    /// </para>
    /// <para>
    /// Handlers are registered as <strong>transient</strong>. They are resolved from the
    /// per-message scope created by the receive pipeline or by <c>InboxProcessor</c> — each
    /// invocation gets a fresh handler instance. The registry is populated before the
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

    /// <summary>
    /// Registers the in-memory transport: an <see cref="IMessageTransport"/> backed by an
    /// <see cref="InMemoryBroker"/>, and its consume loop.
    /// </summary>
    /// <param name="broker">
    /// The broker to use. Supply one to share a queue between successive hosts; omitted, a new
    /// broker is created for this container.
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Another <see cref="IMessageTransport"/> is already registered.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The only feature of the bus that registers a hosted service: the loop that takes each
    /// envelope off the broker and runs it inline through the receive pipeline. See
    /// <see cref="InMemoryBroker"/> for redelivery, dead-lettering and shutdown.
    /// </para>
    /// <para>
    /// Calling it again replaces the broker when one is supplied and is otherwise a no-op. A
    /// different transport registered first is refused rather than silently shadowed.
    /// </para>
    /// </remarks>
    public MessagingBuilder UseInMemoryTransport(InMemoryBroker? broker = null)
    {
        var existing = Services.FirstOrDefault(d =>
            d.ServiceType == typeof(IMessageTransport) && !d.IsKeyedService);

        if (existing is not null && existing.ImplementationType != typeof(InMemoryTransport))
        {
            throw new InvalidOperationException(
                "UseInMemoryTransport() was called, but another IMessageTransport is already " +
                $"registered ({existing.ImplementationType?.FullName ?? "a factory or instance"}). " +
                "A host has one transport; remove one of the two registrations.");
        }

        if (broker is not null)
        {
            Services.RemoveAll<InMemoryBroker>();
            Services.AddSingleton(broker);
        }
        else
        {
            Services.TryAddSingleton(_ => new InMemoryBroker());
        }

        Services.TryAddSingleton<IMessageTransport, InMemoryTransport>();
        Services.AddHostedService<InMemoryTransportWorker>();

        return this;
    }

    /// <summary>
    /// Configures the System.Text.Json options of the default serializer: converters, naming,
    /// a <c>JsonSerializerContext</c> added to <c>TypeInfoResolverChain</c>.
    /// </summary>
    /// <param name="configure">The configuration callback. Applied once, through <c>IOptions</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The options start from <see cref="JsonSerializerDefaults.Web"/> and are used both to
    /// serialize on publish and to deserialize on receive, so a converter registered here applies
    /// in both directions. Repeated calls accumulate, in call order.
    /// </para>
    /// <para>
    /// A source-generated context does not replace reflection: when reflection is enabled, a
    /// reflection resolver is appended after the host's resolvers, so a type the context does not
    /// declare still serializes. Insert a context at the front of the chain to take precedence.
    /// </para>
    /// <para>
    /// Has no effect when the host registered its own <see cref="IMessageSerializer"/>.
    /// </para>
    /// </remarks>
    public MessagingBuilder ConfigureJson(Action<JsonSerializerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure<MessagingJsonOptions>(options => configure(options.SerializerOptions));
        return this;
    }

    /// <summary>Registers a send middleware, run on every publication in registration order.</summary>
    /// <typeparam name="TMiddleware">The middleware type. Resolved per publishing scope.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>Registering one type twice registers it once, at its first position.</remarks>
    public MessagingBuilder AddSendMiddleware<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>()
        where TMiddleware : class, IMessageSendMiddleware
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IMessageSendMiddleware, TMiddleware>());
        return this;
    }

    /// <summary>
    /// Registers a receive middleware, run in each handler's message scope in registration order,
    /// after the scope initializers and before the handler.
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware type. Resolved per message scope.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>Registering one type twice registers it once, at its first position.</remarks>
    public MessagingBuilder AddReceiveMiddleware<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>()
        where TMiddleware : class, IMessageReceiveMiddleware
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IMessageReceiveMiddleware, TMiddleware>());
        return this;
    }

    /// <summary>
    /// Registers a scope initializer, run in each message scope after its execution context is
    /// filled and before any other service of the scope is resolved.
    /// </summary>
    /// <typeparam name="TInitializer">The initializer type. Resolved per message scope.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>Registering one type twice registers it once, at its first position.</remarks>
    public MessagingBuilder AddScopeInitializer<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TInitializer>()
        where TInitializer : class, IMessageScopeInitializer
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IMessageScopeInitializer, TInitializer>());
        return this;
    }
}
