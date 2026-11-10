using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging.Spi;

/// <summary>
/// The seam a store provider calls to switch on the outbox and inbox engine.
/// </summary>
/// <remarks>
/// <para>
/// <b>Transitional, until #188.</b> Since #186 <c>AddMicroKitMessaging()</c> registers no hosted
/// service: the bus works without persistence, and the outbox and inbox workers require stores. A
/// store provider — <c>AddEfCoreOutbox&lt;TContext&gt;()</c> today — calls
/// <see cref="AddOutboxInboxProcessing"/> once it has registered those stores. #188 replaces this
/// with opt-in durability on the bus.
/// </para>
/// <para>
/// Application code does not call it; it lives in the SPI namespace for that reason.
/// </para>
/// </remarks>
public static class OutboxInboxProcessingExtensions
{
    /// <summary>
    /// Registers the outbox and inbox engine: the four hosted workers (outbox, outbox retention,
    /// inbox, inbox retention), the processors, the coordinators and the inbox metrics.
    /// </summary>
    /// <param name="builder">The messaging builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// Idempotent: every registration is a <c>TryAdd</c>, so two store providers calling it, or
    /// one calling it twice, register each service once. It registers no store: the caller does.
    /// </remarks>
    public static MessagingBuilder AddOutboxInboxProcessing(this MessagingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.TryAddScoped<IOutboxProcessor, OutboxProcessor>();
        services.TryAddScoped<IOutboxCoordinator, SharedDbOutboxCoordinator>();
        services.AddHostedService<OutboxWorker>();

        // Retention. Without it DeleteProcessedAsync has no caller, RetentionDays is read by
        // nothing, and the outbox grows without bound in production.
        services.AddHostedService<OutboxRetentionWorker>();

        services.TryAddScoped<IInboxProcessor, InboxProcessor>();
        services.TryAddScoped<IInboxCoordinator, SharedDbInboxCoordinator>();
        services.AddHostedService<InboxWorker>();

        // Inbox retention. The window is deliberately longer than the outbox's and must stay
        // that way: the inbox only deduplicates messages it still holds, so deleting early
        // reopens reprocessing rather than merely losing history.
        services.AddHostedService<InboxRetentionWorker>();

        // Ingestion and drain counters. Owns its Meter rather than taking IMeterFactory, so no
        // host is obliged to call AddMetrics(); subscribe with
        // AddMeter(MessagingDiagnostics.InboxMeterName).
        services.TryAddSingleton<InboxMetrics>();

        return builder;
    }
}
