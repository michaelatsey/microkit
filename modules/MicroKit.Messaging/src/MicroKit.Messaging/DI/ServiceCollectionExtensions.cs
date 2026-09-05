using MicroKit.Messaging.Execution;
using MicroKit.Messaging.Outbox;
using MicroKit.Messaging.Processing;
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
    /// Registers MicroKit.Messaging core services: outbox and inbox workers, the
    /// in-process execution scope factory, and the handler registry.
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
    /// <strong>Required services NOT registered here:</strong>
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
    /// The outbox and inbox workers will log a critical error and stop if required services
    /// are missing at runtime.
    /// </para>
    /// <para>
    /// <b><c>IMessageSerializer</c> IS registered here</b>, as a <c>TryAdd</c>ed
    /// <c>SystemTextJsonMessageSerializer</c> default. It belongs to this method rather than to an
    /// optional builder call because this method registers <c>InboxProcessor</c> and
    /// <c>OutboxMessageFactory</c> unconditionally and both require one (ADR-MSG-019).
    /// <b>To supply your own, register it BEFORE calling this method</b> — a registration made
    /// afterwards loses to the default that is already in the collection. Neither
    /// <c>AddIntegrationEventPublishing()</c> nor <c>AddMediatRDomainEvents()</c> registers a
    /// serializer any more; both are extensions on the builder this method returns, so a
    /// <c>TryAdd</c> in either could never have been reached.
    /// </para>
    /// <para>
    /// <b>One check does run at startup here:</b> <c>InboxIngestionValidator</c> fails the host when
    /// message handlers are registered while nothing in this release produces the inbox rows that
    /// would reach them (ADR-MSG-019). It is contributed with <c>TryAddEnumerable</c>, so calling
    /// this method twice yields one validator.
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
        // the two types that require it — InboxProcessor and OutboxMessageFactory — are registered
        // by this method unconditionally. It used to come from AddInProcessTransport(), and when
        // that was deleted (ADR-MSG-019) a host composing plain outbox + transport was left with a
        // registered InboxProcessor it could not activate: a gap that surfaces at the first worker
        // tick, not at composition. TryAdd, so a host registering its own beforehand keeps it.
        services.TryAddSingleton<IMessageSerializer, SystemTextJsonMessageSerializer>();

        services.AddScoped<IOutboxProcessor, OutboxProcessor>();
        services.AddScoped<IOutboxCoordinator, SharedDbOutboxCoordinator>();
        services.AddHostedService<OutboxWorker>();

        // Retention. Without it DeleteProcessedAsync has no caller, RetentionDays is read by
        // nothing, and the outbox grows without bound in production.
        services.AddHostedService<OutboxRetentionWorker>();

        services.AddScoped<IInboxProcessor, InboxProcessor>();
        services.AddScoped<IInboxCoordinator, SharedDbInboxCoordinator>();
        services.AddHostedService<InboxWorker>();

        // Inbox retention. The window is deliberately longer than the outbox's and must stay
        // that way: the inbox only deduplicates messages it still holds, so deleting early
        // reopens reprocessing rather than merely losing history.
        services.AddHostedService<InboxRetentionWorker>();

        // Ingestion counters. Owns its Meter rather than taking IMeterFactory, so no host is
        // obliged to call AddMetrics(); subscribe with AddMeter(InboxMetrics.MeterName).
        // No producer records into them in this release — the counters belong to the ingestion
        // seam that ADR-MSG-019 defers, and are left registered rather than churned out and back.
        services.TryAddSingleton<InboxMetrics>();

        // Fails the host when handlers are registered but nothing writes the inbox rows that would
        // reach them. TryAddEnumerable so a second AddMicroKitMessaging() contributes one validator
        // rather than two; ServiceDescriptor rather than AddHostedService<T>() for the same reason,
        // since AddHostedService is a plain Add and would stack.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, InboxIngestionValidator>());

        return new MessagingBuilder(services, registry);
    }
}
