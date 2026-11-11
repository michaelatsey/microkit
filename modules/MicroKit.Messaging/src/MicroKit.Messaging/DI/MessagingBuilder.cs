using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MicroKit.Messaging.Dispatch;
using MicroKit.Messaging.Serialization;
using MicroKit.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging;

/// <summary>
/// The composition of MicroKit.Messaging, handed to the lambda of
/// <see cref="ServiceCollectionExtensions.AddMicroKitMessaging"/>: the service name, what the
/// service publishes, its handlers, its transport, its options, and its middleware.
/// </summary>
public sealed class MessagingBuilder
{
    private readonly MessagingComposition _composition;
    private readonly int _serviceNameSlot;
    private string? _serviceName;

    internal MessagingBuilder(IServiceCollection services, MessagingComposition composition)
    {
        Services = services;
        _composition = composition;
        _serviceNameSlot = composition.OpenServiceNameSlot();
    }

    /// <summary>
    /// Gets the underlying service collection for advanced registrations.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Gets or sets the name of this service: the <c>Source</c> of every message it publishes and
    /// the endpoint it consumes from. <see langword="null"/> leaves it unset.
    /// </summary>
    /// <value>The service name, or <see langword="null"/>.</value>
    /// <exception cref="ArgumentException">The value is empty or whitespace.</exception>
    /// <remarks>
    /// <para>
    /// Takes precedence over everything else: <c>bus.Configure(...)</c>, the
    /// <c>MicroKit:Messaging:ServiceName</c> configuration key, and the host's
    /// <c>IHostEnvironment.ApplicationName</c>, in that order below it. With none of them set,
    /// the host fails to start.
    /// </para>
    /// <para>
    /// Two <c>AddMicroKitMessaging</c> calls setting different values fail at start: a service has
    /// one name.
    /// </para>
    /// </remarks>
    public string? ServiceName
    {
        get => _serviceName;
        set
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A service name cannot be empty or whitespace. Set null to leave it unset.",
                    nameof(value));
            }

            _serviceName = value;
            _composition.SetServiceName(_serviceNameSlot, value);
        }
    }

    /// <summary>Declares that this service publishes <typeparamref name="TMessage"/>.</summary>
    /// <typeparam name="TMessage">
    /// The message type. Must carry <see cref="MessageContractAttribute"/>, read here, once.
    /// </typeparam>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Publications are declared, not discovered.</b> Without the declaration, startup could
    /// not detect a duplicated contract name or a publication with no route; with it, both fail at
    /// host start rather than on the one code path that publishes the message.
    /// </para>
    /// <para>
    /// A missing <see cref="MessageContractAttribute"/> is reported at host start with the other
    /// composition problems, not thrown here. Declaring one type twice is a no-op. Publishing a
    /// contract also binds its name for receiving, so a service handling its own publications
    /// declares nothing twice.
    /// </para>
    /// </remarks>
    public MessagingBuilder Publishes<TMessage>()
        where TMessage : class, IIntegrationEvent
    {
        _composition.AddPublication(typeof(TMessage));
        return this;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the handler of every message type it handles:
    /// one subscription per closed <see cref="IMessageHandler{T}"/> it implements.
    /// </summary>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="THandler"/> is abstract, an open generic, or implements no
    /// <see cref="IMessageHandler{T}"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>A handler is its subscription.</b> The contract comes from each handled type's
    /// <see cref="MessageContractAttribute"/>, with no separate declaration; a handled type without
    /// one is reported at host start.
    /// </para>
    /// <para>
    /// <b>Registering the same handler twice is a no-op</b>: one consumer, one invocation per
    /// message. A handler of several message types is one consumer per type, each reaching its own
    /// method, on the bus and on the inbox alike.
    /// </para>
    /// <para>
    /// Handlers are registered as <strong>transient</strong> and resolved from the per-message
    /// scope, so each invocation gets a fresh instance.
    /// </para>
    /// </remarks>
    public MessagingBuilder AddHandler<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)]
        THandler>()
        where THandler : class
    {
        AddHandlerCore(typeof(THandler));
        return this;
    }

    /// <summary>
    /// Registers every non-abstract, non-generic class of the assembly declaring
    /// <typeparamref name="TMarker"/> that implements <see cref="IMessageHandler{T}"/>, exactly as
    /// <see cref="AddHandler{THandler}"/> would.
    /// </summary>
    /// <typeparam name="TMarker">Any type of the assembly to scan.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Scanning reads the assembly's types by reflection, which trimming can break: a handler the
    /// trimmer removed is silently not registered. In a trimmed application, register handlers
    /// with <see cref="AddHandler{THandler}"/>.
    /// </para>
    /// <para>
    /// A type that fails to load — one whose own dependency is not deployed — does not fail the
    /// scan: the types that loaded are registered, and the host logs one <c>Warning</c> at start
    /// naming the assembly and the types that failed (event 3016). It does not fail the start
    /// either, since a type that failed to load is not necessarily a handler.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(
        "Scans the assembly for IMessageHandler<T> implementations; trimming can remove a handler " +
        "that nothing else references. Use AddHandler<THandler>() in a trimmed application.")]
    public MessagingBuilder AddHandlersFromAssemblyContaining<TMarker>()
    {
        var assembly = typeof(TMarker).Assembly;
        return AddHandlersFrom(assembly.GetName().Name ?? assembly.FullName ?? "?", assembly.GetTypes);
    }

    /// <summary>
    /// The scan of <see cref="AddHandlersFromAssemblyContaining{TMarker}"/>, over a type source a
    /// test can make fail.
    /// </summary>
    /// <param name="assembly">The scanned assembly's name, for the start-up warning.</param>
    /// <param name="getTypes">The assembly's <c>GetTypes</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    [RequiresUnreferencedCode("Registers handler types discovered at run time.")]
    internal MessagingBuilder AddHandlersFrom(string assembly, Func<Type[]> getTypes)
    {
        var types = LoadableTypes(getTypes, ex => _composition.RecordUnloadedTypes(assembly, ex));
        foreach (var type in types)
        {
            if (type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters
                && HandledTypes(type).Count > 0)
            {
                AddHandlerCore(type);
            }
        }

        return this;
    }

    /// <summary>
    /// The types an assembly can load. A type whose own dependency is not deployed makes
    /// <c>GetTypes()</c> throw <see cref="System.Reflection.ReflectionTypeLoadException"/> for the
    /// whole assembly; the types that did load are still scanned, and the failure is handed to
    /// <paramref name="onPartialLoad"/> — recorded, then logged once at start, never skipped
    /// silently.
    /// </summary>
    /// <param name="getTypes">The assembly's <c>GetTypes</c>.</param>
    /// <param name="onPartialLoad">Receives the exception when some types failed to load.</param>
    /// <returns>Every type that loaded.</returns>
    internal static IEnumerable<Type> LoadableTypes(
        Func<Type[]> getTypes, Action<System.Reflection.ReflectionTypeLoadException> onPartialLoad)
    {
        try
        {
            return getTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            onPartialLoad(ex);
            return [.. ex.Types.OfType<Type>()];
        }
    }

    /// <summary>
    /// Configures <see cref="MessagingOptions"/>. Applied after the configuration binding, so it
    /// overrides values read from <c>MicroKit:Messaging</c>; <see cref="ServiceName"/>, when set,
    /// overrides the service name it sets.
    /// </summary>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>The same builder, for chaining.</returns>
    public MessagingBuilder Configure(Action<MessagingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Registers <c>TransportOutboxDispatcher</c>, which routes <see cref="MessageKind.Contract"/>
    /// outbox rows to the registered <see cref="IMessageTransport"/>. Called by
    /// <see cref="UseInMemoryTransport"/>.
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Two registrations, because the seam has two slots.</b> The dispatcher goes into the keyed
    /// slot named by <see cref="OutboxDispatcherKeys.Standard"/>, which only this package ever
    /// writes; a forwarder then claims the unkeyed <see cref="IOutboxDispatcher"/> slot that
    /// <c>OutboxProcessor</c> resolves. A decorating package — <c>MicroKit.Messaging.MediatR</c> —
    /// removes the forwarder, takes the unkeyed slot outright, and finds this dispatcher through the
    /// key. That split makes the composition order-independent: before the glue, the forwarder is
    /// removed and the keyed dispatcher wrapped; after it, the keyed <c>TryAdd</c> still lands while
    /// the unkeyed one abstains. Calling this method twice is a no-op.
    /// </para>
    /// <para>
    /// <b>Internal since #187.</b> A transport registration owns it, so the composition is one call.
    /// </para>
    /// </remarks>
    internal MessagingBuilder AddTransportDispatcher()
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

    private void AddHandlerCore(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)]
        Type handlerType)
    {
        if (!handlerType.IsClass || handlerType.IsAbstract || handlerType.ContainsGenericParameters)
        {
            throw new ArgumentException(
                $"'{handlerType.FullName}' cannot be a handler: a handler is a concrete, closed class.",
                nameof(handlerType));
        }

        var handled = HandledTypes(handlerType);
        if (handled.Count == 0)
        {
            throw new ArgumentException(
                $"'{handlerType.FullName}' implements no IMessageHandler<T>, so it handles nothing. " +
                "Implement IMessageHandler<T> for each message type it handles.",
                nameof(handlerType));
        }

        Services.TryAddTransient(handlerType);

        foreach (var messageType in handled)
        {
            _composition.AddHandler(handlerType, messageType);
        }
    }

    private static List<Type> HandledTypes(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type handlerType)
        => [.. handlerType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMessageHandler<>))
            .Select(i => i.GetGenericArguments()[0])];

    /// <summary>
    /// Registers the in-memory transport: an <see cref="IMessageTransport"/> backed by an
    /// <see cref="InMemoryBroker"/>, its consume loop, and the dispatcher that routes outbox
    /// contract rows to it.
    /// </summary>
    /// <param name="broker">
    /// The broker to use. Supply one to connect several hosts — successive or concurrent — through
    /// one broker; omitted, a new broker is created for this container.
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Another <see cref="IMessageTransport"/> is already registered.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The only feature of the bus that registers a hosted service: the loop that takes each
    /// envelope off this service's endpoint and runs it inline through the receive pipeline. The
    /// endpoint is the <see cref="ServiceName"/>, bound at host start to the contracts the
    /// service's handlers handle. See <see cref="InMemoryBroker"/> for the routing, redelivery,
    /// dead-lettering and shutdown.
    /// </para>
    /// <para>
    /// It also registers the outbox's transport dispatcher (<c>TransportOutboxDispatcher</c>), so
    /// a host with <c>AddEfCoreOutbox&lt;TContext&gt;()</c> sends its contract rows here with no
    /// further call — in either order with <c>AddMediatRDomainEvents()</c>.
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

        Services.TryAddSingleton<InMemoryEndpointBinder>();
        Services.TryAddSingleton<IMessageTransport, InMemoryTransport>();
        Services.AddHostedService<InMemoryTransportWorker>();
        AddTransportDispatcher();

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
