using System.Linq.Expressions;
using System.Reflection;

namespace MicroKit.MediatR;

/// <summary>
/// Fluent builder for MicroKit.MediatR configuration.
/// Passed to the <see cref="ServiceCollectionExtensions.AddMicroKitMediatR"/> callback.
/// </summary>
public sealed class MediatRBuilder
{
    private readonly List<Assembly> _assemblies = [];

    internal MediatRBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>Gets the underlying service collection for custom registrations.</summary>
    public IServiceCollection Services { get; }

    internal IReadOnlyList<Assembly> Assemblies => _assemblies;

    /// <summary>Adds <paramref name="assembly"/> to the handler and domain event scan.</summary>
    /// <param name="assembly">The assembly to scan for handlers and domain event notifications.</param>
    public MediatRBuilder FromAssembly(Assembly assembly)
    {
        _assemblies.Add(assembly);
        return this;
    }

    /// <summary>Adds the assembly containing <typeparamref name="T"/> to the scan.</summary>
    /// <typeparam name="T">Any type in the assembly to scan.</typeparam>
    public MediatRBuilder FromAssemblyContaining<T>()
        => FromAssembly(typeof(T).Assembly);

    /// <summary>
    /// Registers an open-generic behavior type in the pipeline at the specified lifetime.
    /// Used by <c>Add*Behavior()</c> extension methods in <c>MicroKit.MediatR.Behaviors</c>.
    /// Behaviors are executed in registration order — call this in <see cref="PipelineOrder"/> sequence.
    /// </summary>
    /// <param name="openBehaviorType">The open generic behavior type, e.g. <c>typeof(LoggingBehavior&lt;,&gt;)</c>.</param>
    /// <param name="lifetime">Service lifetime. Transient is the default and recommended value.</param>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Registering open-generic behaviors uses reflection and is not trim-compatible.")]
    public MediatRBuilder AddOpenBehavior(Type openBehaviorType, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        Services.Add(new ServiceDescriptor(
            typeof(IPipelineBehavior<,>),
            openBehaviorType,
            lifetime));
        return this;
    }
}

/// <summary>Extension methods for registering MicroKit.MediatR with the DI container.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers MicroKit.MediatR: MediatR engine, all handler adapters, <see cref="IDomainEventDispatcher"/>,
    /// and the domain event notification factory. Call <c>builder.FromAssemblyContaining&lt;T&gt;()</c>
    /// inside <paramref name="configure"/> to specify the assemblies to scan.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional builder callback for assembly scan and behavior registration.</param>
    /// <returns>The service collection for chaining.</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Assembly scanning for MicroKit handler registration uses reflection and is not compatible with trimming or NativeAOT. " +
        "Ensure all handler assemblies are preserved, or use a source-generated registration approach.")]
    public static IServiceCollection AddMicroKitMediatR(
        this IServiceCollection services,
        Action<MediatRBuilder>? configure = null)
    {
        var builder = new MediatRBuilder(services);
        configure?.Invoke(builder);

        var assemblies = builder.Assemblies.ToArray();

        // Register MediatR engine
        services.AddMediatR(cfg =>
        {
            foreach (var assembly in assemblies)
                cfg.RegisterServicesFromAssembly(assembly);
        });

        // Scan for MicroKit handlers and register adapters + domain event factory
        var notificationMap = new Dictionary<Type, Func<IEvent, INotification>>();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
            {
                RegisterCommandHandlers(services, type);
                RegisterQueryHandlers(services, type);
                RegisterStreamQueryHandlers(services, type);
                RegisterDomainEventHandlers(services, type, notificationMap);
            }
        }

        // Register the notification factory as singleton
        services.AddSingleton<IDomainEventNotificationFactory>(
            new DomainEventNotificationFactory(notificationMap));

        // Register dispatcher as scoped
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        return services;
    }

    private static void RegisterCommandHandlers(IServiceCollection services, Type type)
    {
        // ICommandHandler<TCommand> (void)
        foreach (var iface in type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<>)))
        {
            var typeArgs = iface.GetGenericArguments(); // [TCommand]
            services.AddScoped(iface, type);
            var adapterType = typeof(CommandHandlerAdapter<>).MakeGenericType(typeArgs);
            var mediatRHandlerType = typeof(IRequestHandler<>).MakeGenericType(typeArgs);
            services.AddScoped(mediatRHandlerType, adapterType);
        }

        // ICommandHandler<TCommand, TResult>
        foreach (var iface in type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
        {
            var typeArgs = iface.GetGenericArguments(); // [TCommand, TResult]
            services.AddScoped(iface, type);
            var adapterType = typeof(CommandHandlerAdapter<,>).MakeGenericType(typeArgs);
            var mediatRHandlerType = typeof(IRequestHandler<,>).MakeGenericType(typeArgs);
            services.AddScoped(mediatRHandlerType, adapterType);
        }
    }

    private static void RegisterQueryHandlers(IServiceCollection services, Type type)
    {
        foreach (var iface in type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
        {
            var typeArgs = iface.GetGenericArguments(); // [TQuery, TResult]
            services.AddScoped(iface, type);
            var adapterType = typeof(QueryHandlerAdapter<,>).MakeGenericType(typeArgs);
            var mediatRHandlerType = typeof(IRequestHandler<,>).MakeGenericType(typeArgs);
            services.AddScoped(mediatRHandlerType, adapterType);
        }
    }

    private static void RegisterStreamQueryHandlers(IServiceCollection services, Type type)
    {
        foreach (var iface in type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamQueryHandler<,>)))
        {
            var typeArgs = iface.GetGenericArguments(); // [TQuery, TResult]
            services.AddScoped(iface, type);
            var adapterType = typeof(StreamQueryHandlerAdapter<,>).MakeGenericType(typeArgs);
            var mediatRHandlerType = typeof(IStreamRequestHandler<,>).MakeGenericType(typeArgs);
            services.AddScoped(mediatRHandlerType, adapterType);
        }
    }

    private static void RegisterDomainEventHandlers(
        IServiceCollection services,
        Type type,
        Dictionary<Type, Func<IEvent, INotification>> notificationMap)
    {
        foreach (var iface in type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<,>)))
        {
            var typeArgs = iface.GetGenericArguments(); // [TEvent, TNotification]
            var eventType = typeArgs[0];
            var notificationType = typeArgs[1];

            services.AddScoped(iface, type);
            var adapterType = typeof(DomainEventHandlerAdapter<,>).MakeGenericType(typeArgs);
            var notificationHandlerType = typeof(INotificationHandler<>).MakeGenericType(notificationType);
            services.AddScoped(notificationHandlerType, adapterType);

            // Build notification factory: (IEvent e) => new TNotification((TEvent)e)
            if (notificationMap.TryGetValue(eventType, out _))
                throw new InvalidOperationException(
                    $"Multiple domain event handlers found for event type '{eventType.Name}' with different notification types. " +
                    $"Each event type must map to exactly one notification type (ADR-005).");

            notificationMap[eventType] = BuildNotificationFactory(eventType, notificationType);
        }
    }

    private static Func<IEvent, INotification> BuildNotificationFactory(Type eventType, Type notificationType)
    {
        // Find the constructor that takes (TEvent domainEvent)
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

// Internal implementation of IDomainEventNotificationFactory
file sealed class DomainEventNotificationFactory(
    Dictionary<Type, Func<IEvent, INotification>> map) : IDomainEventNotificationFactory
{
    public INotification Create(IEvent domainEvent)
    {
        var eventType = domainEvent.GetType();
        if (!map.TryGetValue(eventType, out var factory))
            throw new InvalidOperationException(
                $"No domain event notification registered for event type '{eventType.Name}'. " +
                $"Ensure the assembly containing the handler is passed to MediatRBuilder.FromAssembly().");

        return factory(domainEvent);
    }
}
