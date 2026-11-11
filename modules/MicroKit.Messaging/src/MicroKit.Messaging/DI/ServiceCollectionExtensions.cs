using MicroKit.Messaging.Bus;
using MicroKit.Messaging.Execution;
using MicroKit.Messaging.Outbox;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Serialization;
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
    /// and a store registered without the engine that drains it. Every composition problem is
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
    /// loop. The outbox and inbox engine is registered by the store provider that needs it,
    /// through <see cref="Spi.OutboxInboxProcessingExtensions.AddOutboxInboxProcessing"/> (which
    /// <c>AddEfCoreOutbox&lt;TContext&gt;()</c> calls) — a transitional step that #188 replaces.
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
        composition.Attach(builder);
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

        // Names the outbox row whose dispatch is running in this scope, so a notification handler
        // that publishes an integration event stamps OriginMessageId on the contract row without
        // the identity being threaded through IPublisher.Publish and a handler signature. Written
        // by OutboxProcessor on the scope it received — never by the scope factory, because a host
        // may supply its own and a dropped origin disables deduplication silently rather than
        // throwing. See OriginMessageHolder.
        services.TryAddScoped<OriginMessageHolder>();

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

        // The receiving seam a transport provider hands arriving envelopes to, and the counters it
        // records (#187: here rather than on a separate consumption call). Singleton: it creates
        // its own execution scope per envelope.
        services.TryAddSingleton<IEnvelopeReceiver, EnvelopeReceiver>();
        services.TryAddSingleton<InboxMetrics>();

        // The bus (#186). Scoped, because it reads the ambient tenant, correlation and causation
        // from the scope's IExecutionContext — never from an AsyncLocal. IMessageBus and
        // IEventPublisher resolve to one instance per scope.
        services.TryAddScoped<MessageBus>();
        services.TryAddScoped<IMessageBus>(sp => sp.GetRequiredService<MessageBus>());
        services.TryAddScoped<IEventPublisher>(sp => sp.GetRequiredService<MessageBus>());

        // The receive pipeline, driven by a transport's consume loop. Singleton: every scope it
        // needs is created per handler through IExecutionScopeFactory.
        services.TryAddSingleton<MessageReceivePipeline>();

    }
}
