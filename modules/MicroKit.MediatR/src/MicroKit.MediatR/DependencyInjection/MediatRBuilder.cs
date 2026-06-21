using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace MicroKit.MediatR.DependencyInjection;

/// <summary>
/// Fluent builder for MicroKit.MediatR configuration.
/// Passed to the <see cref="ServiceCollectionExtensions.AddMicroKitMediatR"/> callback.
/// </summary>
public sealed class MediatRBuilder
{
    private readonly List<Assembly> _assemblies = [];
    private readonly HashSet<Type> _registeredBehaviors = [];

    internal MediatRBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>Gets the underlying service collection for custom registrations.</summary>
    public IServiceCollection Services { get; }

    internal IReadOnlyList<Assembly> Assemblies => _assemblies;

    /// <summary>Adds <paramref name="assembly"/> to the handler and domain event scan.</summary>
    /// <param name="assembly">The assembly to scan for handlers and domain event notifications.</param>
    /// <returns>The current builder for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="assembly"/> has already been registered. Duplicate assemblies cause
    /// double handler registrations and misleading startup errors for domain event handlers.
    /// </exception>
    public MediatRBuilder FromAssembly(Assembly assembly)
    {
        // HIGH #7: detect duplicate assembly before scanning to prevent misleading startup errors.
        // Without this guard, scanning the same assembly twice causes the domain event notification
        // map to throw "multiple handlers with different notification types" — a misleading message
        // when the real cause is a duplicated FromAssembly/FromAssemblyContaining call.
        if (_assemblies.Contains(assembly))
            throw new ArgumentException(
                $"Assembly '{assembly.GetName().Name}' was already registered. " +
                $"Passing the same assembly twice causes duplicate handler/adapter DI registrations " +
                $"and a misleading startup error for domain event handlers. " +
                $"If you called FromAssemblyContaining<T>() with two types from the same assembly, use a single call.",
                nameof(assembly));

        _assemblies.Add(assembly);
        return this;
    }

    /// <summary>Adds the assembly containing <typeparamref name="T"/> to the scan.</summary>
    /// <typeparam name="T">Any type in the assembly to scan.</typeparam>
    /// <returns>The current builder for chaining.</returns>
    public MediatRBuilder FromAssemblyContaining<T>()
        => FromAssembly(typeof(T).Assembly);

    /// <summary>
    /// Registers an open-generic behavior type in the pipeline at the specified lifetime.
    /// Used by <c>Add*Behavior()</c> extension methods in <c>MicroKit.MediatR.Behaviors</c>.
    /// Behaviors execute in registration order — call this in <see cref="PipelineOrder"/> sequence.
    /// </summary>
    /// <param name="openBehaviorType">
    /// The open generic behavior type, e.g. <c>typeof(LoggingBehavior&lt;,&gt;)</c>.
    /// Must be an open generic type definition, implement <c>IPipelineBehavior&lt;,&gt;</c>,
    /// and inherit <see cref="BehaviorBase{TRequest,TResponse}"/> (ADR-002).
    /// </param>
    /// <param name="lifetime">
    /// Service lifetime. <see cref="ServiceLifetime.Transient"/> is the default and recommended value.
    /// Avoid <see cref="ServiceLifetime.Singleton"/> if the behavior injects any scoped dependency.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="openBehaviorType"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="openBehaviorType"/> is not an open generic type definition,
    /// does not implement <c>IPipelineBehavior&lt;,&gt;</c>, or does not inherit
    /// <see cref="BehaviorBase{TRequest,TResponse}"/> (ADR-002).
    /// </exception>
    /// <returns>The current builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this behavior type was already registered — double-registration causes the behavior
    /// to execute twice per request with no diagnostic from MediatR.
    /// </exception>
    [RequiresUnreferencedCode("Registering open-generic behaviors uses reflection and is not trim-compatible.")]
    [RequiresDynamicCode("Registering open-generic behaviors calls MakeGenericType at dispatch time and is not NativeAOT-compatible.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "openBehaviorType is provided by a [RequiresUnreferencedCode] caller; the type and its constructors are preserved by the consumer.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "openBehaviorType is provided by a [RequiresUnreferencedCode] caller; its interfaces are preserved by the consumer.")]
    public MediatRBuilder AddOpenBehavior(Type openBehaviorType, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);

        // HIGH #6 — validation gate 1: must be an open generic type definition.
        // A closed type (e.g. typeof(LoggingBehavior<CreateOrderCommand, Result<OrderId>>)) would
        // register a concrete type against IPipelineBehavior<,>, which DI accepts at registration
        // time but fails at resolution with an unhelpful InvalidCastException.
        if (!openBehaviorType.IsGenericTypeDefinition)
            throw new ArgumentException(
                $"'{openBehaviorType.Name}' is not an open generic type definition. " +
                $"Pass the open generic form, e.g. typeof(LoggingBehavior<,>), not a closed type.",
                nameof(openBehaviorType));

        // HIGH #6 — validation gate 2: must implement IPipelineBehavior<,>.
        var implementsPipeline = openBehaviorType
            .GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));
        if (!implementsPipeline)
            throw new ArgumentException(
                $"'{openBehaviorType.Name}' does not implement IPipelineBehavior<TRequest, TResponse>. " +
                $"Only MediatR pipeline behaviors can be registered via AddOpenBehavior.",
                nameof(openBehaviorType));

        // HIGH #6 — validation gate 3: must inherit BehaviorBase<,> (ADR-002).
        // Direct IPipelineBehavior implementations bypass pipeline-order enforcement and the
        // Result<T> detection/failure-construction helpers provided by BehaviorBase.
        var inherits = false;
        var cursor = openBehaviorType.BaseType;
        while (cursor is not null && cursor != typeof(object))
        {
            if (cursor.IsGenericType && cursor.GetGenericTypeDefinition() == typeof(BehaviorBase<,>))
            {
                inherits = true;
                break;
            }
            cursor = cursor.BaseType;
        }
        if (!inherits)
            throw new ArgumentException(
                $"'{openBehaviorType.Name}' does not inherit BehaviorBase<TRequest, TResponse>. " +
                $"All MicroKit pipeline behaviors must inherit BehaviorBase (ADR-002). " +
                $"Direct IPipelineBehavior implementations bypass Order enforcement and Result<T> helpers.",
                nameof(openBehaviorType));

        // HIGH #6 — duplicate guard: calling Add*Behavior() twice for the same type causes
        // that behavior to execute twice per request. MediatR dispatches to ALL registered
        // IPipelineBehavior<TRequest,TResponse> entries and does not deduplicate.
        if (!_registeredBehaviors.Add(openBehaviorType))
            throw new InvalidOperationException(
                $"Behavior '{openBehaviorType.Name}' is already registered in this builder. " +
                $"Registering the same open-generic behavior twice causes it to execute twice per request.");

        Services.Add(new ServiceDescriptor(typeof(IPipelineBehavior<,>), openBehaviorType, lifetime));
        return this;
    }
}

/// <summary>Extension methods for registering MicroKit.MediatR with the DI container.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers MicroKit.MediatR: MediatR engine, all handler adapters, <see cref="IDomainEventDispatcher"/>,
    /// <see cref="IDomainEventHandlerDispatcher"/>, and the domain event notification factory.
    /// Call <c>builder.FromAssemblyContaining&lt;T&gt;()</c> inside <paramref name="configure"/>
    /// to specify the assemblies to scan.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional builder callback for assembly scan and behavior registration.</param>
    /// <returns>The service collection for chaining.</returns>
    [RequiresUnreferencedCode(
        "Assembly scanning for MicroKit handler registration uses reflection and is not compatible with " +
        "trimming or NativeAOT. Ensure all handler assemblies are preserved, or use a source-generated " +
        "registration approach.")]
    [RequiresDynamicCode(
        "Handler registration calls Type.MakeGenericType at startup and is not NativeAOT-compatible. " +
        "All closed handler types must be rooted.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Entry point is annotated [RequiresUnreferencedCode]; callers accept the trimming incompatibility.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "GetInterfaces() is called on handler types that the [RequiresUnreferencedCode] consumer is responsible for preserving.")]
    public static IServiceCollection AddMicroKitMediatR(
        this IServiceCollection services,
        Action<MediatRBuilder>? configure = null)
    {
        var builder = new MediatRBuilder(services);
        configure?.Invoke(builder);

        var assemblies = builder.Assemblies.ToArray();

        // MEDIUM #2 — ORDERING DEPENDENCY: services.AddMediatR must run BEFORE the custom adapter scan below.
        //
        // MediatR's RegisterServicesFromAssembly scans for types implementing IRequestHandler<,>
        // directly. Consumer handler types implement ICommandHandler<,> / IQueryHandler<,> (MicroKit)
        // — NOT IRequestHandler<,> — so MediatR's scan finds nothing in the consumer assemblies and
        // does not conflict with the adapter registrations added next.
        //
        // If this ordering were reversed (custom scan first), MediatR's setup infrastructure
        // might not be fully initialised when adapters attempt to register IRequestHandler<,>
        // entries. Additionally, adapter registrations (AddScoped) would appear before MediatR's
        // own registrations, but since last-registration-wins in MS DI, the adapters would
        // ultimately prevail regardless — the ordering still matters for initialisation safety.
        services.AddMediatR(cfg =>
        {
            foreach (var assembly in assemblies)
                cfg.RegisterServicesFromAssembly(assembly);
        });

        // Phase A: scan for IDomainEventHandler<TEvent> implementations — build handler dispatch map.
        // Phase B: scan for DomainEventNotification<TEvent> subclasses — build notification factory.
        // notificationTypeMap tracks the first notification type seen per event type (ADR-MEDIATR-005 conflict detection).
        // notificationMap holds the compiled dispatch factory per event type (used at runtime by INotificationFactory).
        var handlerDelegates = new Dictionary<Type, List<Func<IServiceProvider, IEvent, CancellationToken, Task>>>();
        var notificationTypeMap = new Dictionary<Type, Type>();
        var notificationMap = new Dictionary<Type, Func<IEvent, INotification>>();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
            {
                // MEDIUM #3: GetInterfaces() is computed once per type and passed to all
                // registration methods, avoiding redundant reflection calls per type.
                var interfaces = type.GetInterfaces();
                RegisterCommandHandlers(services, type, interfaces);
                RegisterQueryHandlers(services, type, interfaces);
                RegisterStreamQueryHandlers(services, type, interfaces);

                // Phase A — domain event handlers
                RegisterDomainEventHandlers(services, type, interfaces, handlerDelegates);

                // Phase B — notification factory (scans concrete DomainEventNotification<TEvent> subclasses)
                RegisterDomainEventNotifications(type, notificationTypeMap, notificationMap);
            }
        }

        // Phase A result: singleton delegate map + scoped dispatcher
        var handlerMap = new HandlerDispatchMap(
            handlerDelegates.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
        services.AddSingleton(handlerMap);
        services.AddScoped<IDomainEventHandlerDispatcher, DomainEventHandlerDispatcher>();

        // Default IDomainEventDispatcher (scoped — injects scoped IDomainEventHandlerDispatcher)
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // Phase B result: notification factory singleton (unchanged contract)
        var notificationFactory = new DomainEventNotificationFactory(notificationMap);
        services.AddSingleton<IDomainEventNotificationFactory>(notificationFactory);
        services.AddSingleton<INotificationFactory>(notificationFactory);

        return services;
    }

    // MEDIUM #2 / MEDIUM #5 — Handler lifetime: AddScoped is a deliberate override
    // of MediatR's Transient default. Reasons:
    //   1. Most handler dependencies (DbContext, repositories, unit-of-work) are Scoped; aligning
    //      handler lifetime prevents captive-dependency issues within a request.
    //   2. A Scoped handler shares the same DI scope as its unit-of-work boundary.

    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "Handler and adapter types are provided by a [RequiresUnreferencedCode] caller; constructors are preserved by the consumer.")]
    private static void RegisterCommandHandlers(IServiceCollection services, Type type, Type[] interfaces)
    {
        foreach (var iface in interfaces
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<>)))
        {
            var typeArgs = iface.GetGenericArguments();
            services.AddScoped(iface, type);
            var adapterType = typeof(CommandHandlerAdapter<>).MakeGenericType(typeArgs);
            var mediatRHandlerType = typeof(IRequestHandler<>).MakeGenericType(typeArgs);
            services.AddScoped(mediatRHandlerType, adapterType);
        }

        foreach (var iface in interfaces
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
        {
            var typeArgs = iface.GetGenericArguments();
            services.AddScoped(iface, type);
            var adapterType = typeof(CommandHandlerAdapter<,>).MakeGenericType(typeArgs);
            var mediatRHandlerType = typeof(IRequestHandler<,>).MakeGenericType(typeArgs);
            services.AddScoped(mediatRHandlerType, adapterType);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "Handler and adapter types are provided by a [RequiresUnreferencedCode] caller; constructors are preserved by the consumer.")]
    private static void RegisterQueryHandlers(IServiceCollection services, Type type, Type[] interfaces)
    {
        foreach (var iface in interfaces
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
        {
            var typeArgs = iface.GetGenericArguments();
            services.AddScoped(iface, type);
            var adapterType = typeof(QueryHandlerAdapter<,>).MakeGenericType(typeArgs);
            var mediatRHandlerType = typeof(IRequestHandler<,>).MakeGenericType(typeArgs);
            services.AddScoped(mediatRHandlerType, adapterType);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "Handler and adapter types are provided by a [RequiresUnreferencedCode] caller; constructors are preserved by the consumer.")]
    private static void RegisterStreamQueryHandlers(IServiceCollection services, Type type, Type[] interfaces)
    {
        foreach (var iface in interfaces
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamQueryHandler<,>)))
        {
            var typeArgs = iface.GetGenericArguments();
            services.AddScoped(iface, type);
            var adapterType = typeof(StreamQueryHandlerAdapter<,>).MakeGenericType(typeArgs);
            var mediatRHandlerType = typeof(IStreamRequestHandler<,>).MakeGenericType(typeArgs);
            services.AddScoped(mediatRHandlerType, adapterType);
        }
    }

    // Phase A: scan for IDomainEventHandler<TEvent> (single type param) implementations.
    // Registers each handler as scoped and builds a compiled delegate for the dispatch map.
    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "Handler types are provided by a [RequiresUnreferencedCode] caller; constructors are preserved by the consumer.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Handler interface GetMethod is called on types preserved by the [RequiresUnreferencedCode] consumer.")]
    private static void RegisterDomainEventHandlers(
        IServiceCollection services,
        Type type,
        Type[] interfaces,
        Dictionary<Type, List<Func<IServiceProvider, IEvent, CancellationToken, Task>>> handlerDelegates)
    {
        foreach (var iface in interfaces
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)))
        {
            var eventType = iface.GetGenericArguments()[0];

            services.AddScoped(iface, type);
            // Also register under the concrete type so the compiled dispatch delegate can call
            // GetRequiredService(concreteType). Interface-keyed lookup returns last-registered only,
            // which would silently drop all-but-one handler in a multi-handler fan-out scenario.
            services.AddScoped(type);

            var compiled = BuildHandlerDelegate(eventType, type);

            if (!handlerDelegates.TryGetValue(eventType, out var list))
            {
                list = [];
                handlerDelegates[eventType] = list;
            }
            list.Add(compiled);
        }
    }

    // Phase B: scan for concrete DomainEventNotification<TEvent> subclasses.
    // Builds the IEvent → INotification factory map used by INotificationFactory / IDomainEventNotificationFactory.
    // ADR-MEDIATR-005: each IEvent type maps to exactly one DomainEventNotification<TEvent> subclass.
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Notification type constructor is available — types are provided by a [RequiresUnreferencedCode] caller.")]
    private static void RegisterDomainEventNotifications(
        Type type,
        Dictionary<Type, Type> notificationTypeMap,
        Dictionary<Type, Func<IEvent, INotification>> notificationMap)
    {
        // Walk the base type chain to find DomainEventNotification<TEvent>
        var cursor = type.BaseType;
        while (cursor is not null && cursor != typeof(object))
        {
            if (cursor.IsGenericType && cursor.GetGenericTypeDefinition() == typeof(DomainEventNotification<>))
            {
                var eventType = cursor.GetGenericArguments()[0];

                // ADR-MEDIATR-005 conflict detection: exactly one notification type per event type.
                if (notificationTypeMap.TryGetValue(eventType, out var existingNotificationType))
                {
                    if (existingNotificationType != type)
                        throw new InvalidOperationException(
                            $"Conflicting notification types for domain event '{eventType.Name}': " +
                            $"notification type '{type.Name}' and a previously registered notification type " +
                            $"'{existingNotificationType.Name}' both derive from " +
                            $"DomainEventNotification<{eventType.Name}>. " +
                            $"Only one notification type per event type is allowed (ADR-MEDIATR-005). " +
                            $"If multiple handlers are needed, have them all implement " +
                            $"INotificationHandler<{existingNotificationType.Name}> — " +
                            $"MediatR dispatches to all registered INotificationHandler<{existingNotificationType.Name}> implementations.");
                    // Same type registered twice (shouldn't happen with the assembly dedup guard) — skip.
                }
                else
                {
                    notificationTypeMap[eventType] = type;
                    notificationMap[eventType] = BuildNotificationFactory(eventType, type);
                }
                break;
            }
            cursor = cursor.BaseType;
        }
    }

    // Builds a compiled Func<IServiceProvider, IEvent, CancellationToken, Task> for a single handler type.
    // At dispatch time: resolve the concrete handler from sp, cast evt to TEvent, invoke Handle.
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "Handler interface GetMethod is called on types preserved by the [RequiresUnreferencedCode] consumer.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Handler types and their methods are preserved by the [RequiresUnreferencedCode] consumer.")]
    private static Func<IServiceProvider, IEvent, CancellationToken, Task> BuildHandlerDelegate(
        Type eventType, Type concreteHandlerType)
    {
        var spParam = Expression.Parameter(typeof(IServiceProvider), "sp");
        var evtParam = Expression.Parameter(typeof(IEvent), "evt");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var handlerInterfaceType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);

        // sp.GetRequiredService(concreteHandlerType) → object
        var getServiceMethod = typeof(ServiceProviderServiceExtensions)
            .GetMethod(
                nameof(ServiceProviderServiceExtensions.GetRequiredService),
                BindingFlags.Public | BindingFlags.Static,
                null,
                [typeof(IServiceProvider), typeof(Type)],
                null)
            ?? throw new InvalidOperationException(
                "Cannot find ServiceProviderServiceExtensions.GetRequiredService(IServiceProvider, Type).");

        var getService = Expression.Call(
            null,
            getServiceMethod,
            spParam,
            Expression.Constant(concreteHandlerType));

        // Cast to IDomainEventHandler<TEvent>
        var castHandler = Expression.Convert(getService, handlerInterfaceType);

        // Cast evt to TEvent
        var castEvent = Expression.Convert(evtParam, eventType);

        // handler.Handle((TEvent)evt, ct)
        var handleMethod = handlerInterfaceType.GetMethod("Handle")
            ?? throw new InvalidOperationException(
                $"Cannot find Handle method on {handlerInterfaceType.Name}.");

        var handleCall = Expression.Call(castHandler, handleMethod, castEvent, ctParam);

        return Expression.Lambda<Func<IServiceProvider, IEvent, CancellationToken, Task>>(
            handleCall, spParam, evtParam, ctParam).Compile();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Notification type constructor is available — types are provided by a [RequiresUnreferencedCode] caller.")]
    private static Func<IEvent, INotification> BuildNotificationFactory(Type eventType, Type notificationType)
    {
        var ctor = notificationType.GetConstructor([eventType])
            ?? throw new InvalidOperationException(
                $"Notification type '{notificationType.Name}' must have a public constructor accepting '{eventType.Name}'.");

        var param = Expression.Parameter(typeof(IEvent), "domainEvent");
        var cast = Expression.Convert(param, eventType);
        var newExpr = Expression.New(ctor, cast);
        var convertResult = Expression.Convert(newExpr, typeof(INotification));
        return Expression.Lambda<Func<IEvent, INotification>>(convertResult, param).Compile();
    }
}

// Internal implementation of IDomainEventNotificationFactory and INotificationFactory.
// file-scoped: cannot be instantiated or tested in isolation — only exercised through AddMicroKitMediatR.
file sealed class DomainEventNotificationFactory(
    Dictionary<Type, Func<IEvent, INotification>> map) : IDomainEventNotificationFactory, INotificationFactory
{
    // IDomainEventNotificationFactory semantics: throws on miss — callers expect a notification or an error.
    INotification IDomainEventNotificationFactory.Create(IEvent domainEvent)
    {
        var eventType = domainEvent.GetType();
        if (!map.TryGetValue(eventType, out var factory))
            // MEDIUM #8: this error occurs at dispatch time (first call), NOT at DI startup.
            // Missing notification coverage is not validated during AddMicroKitMediatR — only the
            // notification type uniqueness constraint (ADR-MEDIATR-005) is enforced at startup.
            throw new InvalidOperationException(
                $"No domain event notification registered for event type '{eventType.Name}'. " +
                $"This error is raised at dispatch time — missing notification coverage is not detected until " +
                $"IDomainEventNotificationFactory.Create is first called for this event type. " +
                $"Ensure the assembly containing a concrete DomainEventNotification<{eventType.Name}> " +
                $"subclass is passed to MediatRBuilder.FromAssembly().");

        return factory(domainEvent);
    }

    // INotificationFactory semantics: returns null on miss — callers skip events without a notification.
    IDomainEventNotification<IEvent>? INotificationFactory.Create(IEvent domainEvent)
    {
        if (!map.TryGetValue(domainEvent.GetType(), out var factory)) return null;
        return factory(domainEvent) as IDomainEventNotification<IEvent>;
    }
}
