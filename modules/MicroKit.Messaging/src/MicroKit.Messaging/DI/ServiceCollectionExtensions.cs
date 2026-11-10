using MicroKit.Messaging.Bus;
using MicroKit.Messaging.Execution;
using MicroKit.Messaging.Outbox;
using MicroKit.Messaging.Registry;
using MicroKit.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging;

/// <summary>
/// Extension methods for registering MicroKit.Messaging core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers MicroKit.Messaging core services: the message bus (<see cref="IMessageBus"/> and
    /// <see cref="IEventPublisher"/>), the receive pipeline, the contract and handler registries,
    /// the default serializer, and the in-process execution scope factory.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configureOutbox">Optional callback to configure outbox processor options.</param>
    /// <param name="configureInbox">Optional callback to configure inbox processor options.</param>
    /// <returns>
    /// A <see cref="MessagingBuilder"/> for chaining additional registrations
    /// (transports and handlers).
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>No hosted service is registered here (#186).</b> The bus works with no persistence: add
    /// a transport — <see cref="MessagingBuilder.UseInMemoryTransport"/>, which registers its own
    /// consume loop — and the bus publishes and consumes. The outbox and inbox workers, processors
    /// and coordinators are registered by the store provider that needs them, through
    /// <see cref="Spi.OutboxInboxProcessingExtensions.AddOutboxInboxProcessing"/> (which
    /// <c>AddEfCoreOutbox&lt;TContext&gt;()</c> calls) — a transitional step that #188 replaces.
    /// </para>
    /// <para>
    /// <strong>Required by the outbox and inbox path, and NOT registered here:</strong>
    /// <list type="bullet">
    /// <item><description>
    /// <c>IOutboxProcessorStore</c> — must be provided by a store implementation,
    /// e.g. <c>AddEfCoreOutbox()</c> from <c>MicroKit.Messaging.EntityFrameworkCore</c>.
    /// </description></item>
    /// <item><description>
    /// <c>IOutboxRetentionStore</c> — required by the retention worker; supplied by the same
    /// store implementation.
    /// </description></item>
    /// <item><description>
    /// <c>IInboxWriter</c>, <c>IInboxProcessorStore</c>, <c>IInboxSettlementStore</c> and
    /// <c>IInboxRetentionStore</c> — same requirement as above. <c>IInboxSettlementStore</c>
    /// must resolve from the per-message execution scope against the same <c>DbContext</c> the
    /// handler writes through, which is what lets the processed mark commit in the handler's own
    /// transaction.
    /// </description></item>
    /// <item><description>
    /// <c>IOutboxDispatcher</c> — call <see cref="MessagingBuilder.AddTransportDispatcher"/> on the
    /// returned builder, or a broker provider's <c>Add{Provider}Transport()</c>, which calls it.
    /// A host that publishes only domain-event notifications registers none of them and gets its
    /// dispatcher from <c>MicroKit.Messaging.MediatR</c> instead.
    /// </description></item>
    /// </list>
    /// The outbox and inbox workers, once registered, log a critical error and stop if required
    /// services are missing at runtime.
    /// </para>
    /// <para>
    /// <b><c>IMessageSerializer</c> IS registered here</b>, as a <c>TryAdd</c>ed
    /// <c>SystemTextJsonMessageSerializer</c> default, configured through
    /// <see cref="MessagingBuilder.ConfigureJson"/>. It belongs to this method rather than to an
    /// optional builder call because the bus, the receive pipeline and <c>OutboxMessageFactory</c>
    /// all require one (ADR-MSG-019).
    /// <b>To supply your own, register it BEFORE calling this method</b> — a registration made
    /// afterwards loses to the default that is already in the collection. Neither
    /// <c>AddIntegrationEventPublishing()</c> nor <c>AddMediatRDomainEvents()</c> registers a
    /// serializer any more; both are extensions on the builder this method returns, so a
    /// <c>TryAdd</c> in either could never have been reached.
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddMicroKitMessaging(
        this IServiceCollection services,
        Action<OutboxProcessorOptions>? configureOutbox = null,
        Action<InboxProcessorOptions>? configureInbox = null)
    {
        var outboxOptions = new OutboxProcessorOptions();
        configureOutbox?.Invoke(outboxOptions);

        var inboxOptions = new InboxProcessorOptions();
        configureInbox?.Invoke(inboxOptions);

        var registry = new MessageHandlerRegistry();

        services.AddSingleton(registry);
        services.AddSingleton(outboxOptions);
        services.AddSingleton(inboxOptions);
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

        // The contract registry the bus resolves wire types through. The same singleton
        // AddIntegrationEventPublishing() and AddIntegrationEventConsumption() register: TryAdd on
        // both sides, one factory.
        IntegrationEventServiceCollectionExtensions.AddContractRegistrySingleton(services);

        // The bus (#186). Scoped, because it reads the ambient tenant, correlation and causation
        // from the scope's IExecutionContext — never from an AsyncLocal. IMessageBus and
        // IEventPublisher resolve to one instance per scope.
        services.TryAddScoped<MessageBus>();
        services.TryAddScoped<IMessageBus>(sp => sp.GetRequiredService<MessageBus>());
        services.TryAddScoped<IEventPublisher>(sp => sp.GetRequiredService<MessageBus>());

        // The receive pipeline, driven by a transport's consume loop. Singleton: every scope it
        // needs is created per handler through IExecutionScopeFactory.
        services.TryAddSingleton<MessageReceivePipeline>();

        return new MessagingBuilder(services, registry);
    }
}
