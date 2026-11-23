using MicroKit.Messaging.Bus;
using MicroKit.Messaging.Execution;
using MicroKit.Messaging.Local;
using MicroKit.Messaging.Outbox;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Registry;
using MicroKit.Messaging.Serialization;
using MicroKit.Messaging.Stores;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MicroKit.Messaging;

/// <summary>
/// Extension methods for registering MicroKit.Messaging core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers MicroKit.Messaging and composes it in one call: what the service publishes, its
    /// handlers, its transport and its options.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">
    /// The composition: <see cref="MessagingBuilder.ServiceName"/>,
    /// <see cref="MessagingBuilder.Publishes{TMessage}"/>, <see cref="MessagingBuilder.AddHandler{THandler}"/>,
    /// <see cref="MessagingBuilder.UseInMemoryTransport"/>, <see cref="MessagingBuilder.Configure"/>.
    /// </param>
    /// <returns>The builder, for chaining further registrations.</returns>
    /// <remarks>
    /// <para>
    /// <b>The quick start:</b>
    /// <code>
    /// services.AddMicroKitMessaging(bus =>
    /// {
    ///     bus.ServiceName = "shop.orders";
    ///     bus.Publishes&lt;OrderPlaced&gt;();
    ///     bus.AddHandler&lt;OrderPlacedHandler&gt;();
    ///     bus.UseInMemoryTransport();
    /// });
    /// </code>
    /// </para>
    /// <para>
    /// <b>Validated at host start.</b> The options are validated (<c>ValidateOnStart</c>), then the
    /// composition: a published or handled type without <see cref="MessageContractAttribute"/>, two
    /// types claiming one wire type, conflicting service names, a publication with no transport,
    /// a store missing one of its services or failing its provider's checks, an assembly bound to
    /// two stores, and an unbound handler with several stores. Every composition problem is
    /// listed in one <see cref="IntegrationEventConfigurationException"/>, each with its fix.
    /// </para>
    /// <para>
    /// <b>Options</b> bind from the <c>MicroKit:Messaging</c> configuration section when the
    /// container holds an <c>IConfiguration</c>, and keep their defaults when it does not.
    /// Configuration is read <b>once</b>, when the options are first resolved: there is no reload.
    /// Precedence, lowest first: <c>IHostEnvironment.ApplicationName</c> (the service name only),
    /// configuration, <see cref="MessagingBuilder.Configure"/>, <see cref="MessagingBuilder.ServiceName"/>.
    /// </para>
    /// <para>
    /// <b>Calling it again adds to the same composition</b> — one call per module of a modular
    /// monolith works. Two calls setting different <see cref="MessagingBuilder.ServiceName"/>
    /// values fail at start.
    /// </para>
    /// <para>
    /// <b>No hosted service besides the validator is registered here.</b> The bus works with no
    /// persistence: <see cref="MessagingBuilder.UseInMemoryTransport"/> registers its own consume
    /// loop. Durability is opt-in: a store provider — <c>bus.UseEntityFrameworkOutbox&lt;TContext&gt;()</c>
    /// — adds a store through <see cref="Spi.MessageStoreRegistrationExtensions.AddMessageStore"/>,
    /// which registers the engine that drains every store.
    /// </para>
    /// <para>
    /// <b><c>IMessageSerializer</c> IS registered here</b>, as a <c>TryAdd</c>ed
    /// <c>SystemTextJsonMessageSerializer</c> default, configured through
    /// <see cref="MessagingBuilder.ConfigureJson"/>. <b>To supply your own, register it BEFORE
    /// calling this method</b> — a registration made afterwards loses to the default already in
    /// the collection.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static MessagingBuilder AddMicroKitMessaging(
        this IServiceCollection services,
        Action<MessagingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var composition = services
            .Select(d => d.ServiceType == typeof(MessagingComposition) ? d.ImplementationInstance : null)
            .OfType<MessagingComposition>()
            .FirstOrDefault();

        if (composition is null)
        {
            composition = new MessagingComposition();
            AddCore(services, composition);
        }

        var builder = new MessagingBuilder(services, composition);
        configure(builder);
        return builder;
    }

    /// <summary>The registrations made once per service collection, on the first call.</summary>
    private static void AddCore(IServiceCollection services, MessagingComposition composition)
    {
        services.AddSingleton(composition);
        services.AddSingleton(composition.Handlers);

        // FIRST, before the lambda of this call can register any hosted service: the host runs
        // StartingAsync in registration order, and the in-memory transport binds its endpoint in
        // its own StartingAsync. An invalid composition must fail before anything is bound.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, MessagingCompositionValidator>());

        MessagingOptionsSetup.Register(services, composition);

        services.AddSingleton<IExecutionScopeFactory, PassThroughExecutionScopeFactory>();
        services.AddSingleton<OutboxMessageFactory>();

        // Clock and jitter source, both TryAdd so a host that already supplies its own wins.
        // Injected rather than read statically so the outbox retry curve is unit-testable
        // without a wall clock and without a range assertion: TimeProvider fixes "now",
        // Random fixes the jitter draw. Random.Shared is thread-safe (.NET 6+).
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(Random.Shared);

        // IExecutionContext resolves THROUGH a scoped holder, and the indirection is the whole
        // point (L0 finding #21). PassThroughExecutionScopeFactory writes the message-row context
        // into the holder when it creates a scope, so a service that takes IExecutionContext as a
        // CONSTRUCTOR parameter sees it. Registering the context directly — as this used to —
        // meant only a direct GetService call could ever observe the message row, because
        // Microsoft DI activates constructor dependencies from its own scope and never consults
        // the scope's IServiceProvider wrapper. Every constructor-injected consumer silently got
        // a fresh CorrelationId with a null TenantId instead.
        //
        // The holder's default value covers a scope created outside the messaging pipeline: one
        // stable CorrelationId per DI scope, TenantId/CausationId null (ADR-EXEC-001,
        // ADR-MSG-008 §7). A tenant-aware host overrides IExecutionContext via a non-Try
        // AddScoped<IExecutionContext>(), which bypasses the holder and takes hydration on itself.
        //
        // Scoped — never injected into the singleton OutboxMessageFactory (it takes
        // IExecutionContext as a method parameter), so there is no captive dependency.
        services.TryAddScoped<ExecutionContextHolder>();
        services.TryAddScoped<IExecutionContext>(
            sp => sp.GetRequiredService<ExecutionContextHolder>().Context);

        // The stores (#188). The set and the resolver are read from the composition once the
        // container is built; the holder names, per scope, the store of the delivery the engine
        // runs there, so a handler's publication is enlisted in that store. Written by the engine
        // on the scope it received — never by the scope factory, which a host may replace.
        services.TryAddSingleton<IMessageStoreSet>(_ => new StaticMessageStoreSet(composition));
        services.TryAddSingleton(sp => new MessageStoreResolver(composition, sp.GetRequiredService<IMessageStoreSet>()));
        services.TryAddScoped<DeliveryStoreHolder>();

        // The partitioned stores and the instances the engine drains now (#191): what a delivery
        // guarded by a partitioned store is mapped to. Empty, and one read, on a host with none.
        services.TryAddSingleton(sp => new StoreInstanceRegistry(
            composition,
            sp,
            sp.GetRequiredService<ILogger<StoreInstanceRegistry>>(),
            sp.GetRequiredService<OutboxProcessorOptions>(),
            sp.GetRequiredService<TimeProvider>()));

        // The serializer default. It belongs HERE and not on an optional builder method, because
        // the types that require it — the bus, the receive pipeline and OutboxMessageFactory — are
        // registered by this method unconditionally. TryAdd, so a host registering its own
        // beforehand keeps it. Its options start from the Web defaults and are extended by
        // MessagingBuilder.ConfigureJson, through IOptions, and used in both directions.
        services.AddOptions<MessagingJsonOptions>();
        services.TryAddSingleton<IMessageSerializer, SystemTextJsonMessageSerializer>();

        // The contract registry, composed from the declarations of every AddMicroKitMessaging call
        // once the container is built, with the service name as the source. Resolving it applies
        // the contract rules (MessagingCompositionAnalysis), so a container resolved without a
        // host rejects what a host start rejects.
        services.TryAddSingleton(sp => new IntegrationEventRegistry(
            composition,
            sp.GetRequiredService<IOptions<MessagingOptions>>().Value.ServiceName!));

        // The receiving seam a transport provider hands arriving envelopes to — a thin adapter over
        // the receive pipeline (#188) — and the inbox counters. Singletons: every scope they need
        // is created per handler.
        services.TryAddSingleton<IEnvelopeReceiver, EnvelopeReceiver>();
        services.TryAddSingleton<InboxMetrics>();

        // The inbox retry policy, shared by a failed delivery and a store's retry loop (#188).
        services.TryAddSingleton(sp => new InboxRetryPolicy(
            sp.GetRequiredService<InboxProcessorOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<Random>()));

        // The bus (#186). Scoped, because it reads the ambient tenant, correlation and causation
        // from the scope's IExecutionContext — never from an AsyncLocal. IMessageBus and
        // IEventPublisher resolve to one instance per scope.
        services.TryAddScoped<MessageBus>();
        services.TryAddScoped<IMessageBus>(sp => sp.GetRequiredService<MessageBus>());
        services.TryAddScoped<IEventPublisher>(sp => sp.GetRequiredService<MessageBus>());

        // The receive pipeline, driven by a transport's consume loop. Singleton: every scope it
        // needs is created per handler through IExecutionScopeFactory.
        services.TryAddSingleton<MessageReceivePipeline>();

        // The tenants in back-off on each store's inbox (#124), shared by the pipeline's deliveries and
        // the stores' retry loops. Empty, and never consulted past one read, on a host with no store.
        services.TryAddSingleton(static sp => new InboxTenantBackoff(sp.GetRequiredService<InboxProcessorOptions>()));

        // The local endpoint (#188 D7): the consumers the integration packages supply, the sender
        // they enlist or queue through, and — for a host with no store — the in-memory queue. Its
        // worker comes with the first consumer source (AddLocalConsumerSource): a host with no
        // local consumer runs no hosted service of the bus's own.
        services.TryAddSingleton<LocalConsumerRegistry>();
        services.TryAddScoped<ILocalMessageSender, LocalMessageSender>();
        services.TryAddSingleton<LocalQueue>();
    }
}
