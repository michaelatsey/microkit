using MicroKit.Messaging.Execution;
using MicroKit.Messaging.Outbox;
using MicroKit.Messaging.Processing;
using MicroKit.Messaging.Registry;
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
    /// <c>IMessageSerializer</c> and <c>IOutboxDispatcher</c> — call
    /// <see cref="MessagingBuilder.AddInProcessTransport"/> on the returned builder.
    /// </description></item>
    /// </list>
    /// The outbox and inbox workers will log a critical error and stop if required services
    /// are missing at runtime.
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
        services.TryAddSingleton<InboxMetrics>();

        return new MessagingBuilder(services, registry);
    }
}
