namespace MicroKit.Messaging.EntityFrameworkCore;

using MicroKit.Messaging.Publishing;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Extension methods on <see cref="MessagingBuilder"/> for wiring EF Core outbox/inbox stores.
/// </summary>
public static class MessagingBuilderExtensions
{
    /// <summary>
    /// Registers the EF Core persistence adapter for the transactional outbox and inbox.
    /// </summary>
    /// <typeparam name="TContext">
    /// The application's <see cref="DbContext"/> type. Must have
    /// <see cref="ModelBuilderExtensions.ApplyMessagingConfiguration"/> called in
    /// <c>OnModelCreating</c>.
    /// </typeparam>
    /// <param name="builder">The <see cref="MessagingBuilder"/> handed to
    /// <c>AddMicroKitMessaging(bus => ...)</c>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>The name is a misnomer: this wires the inbox too.</b> Each store is registered once as
    /// scoped by concrete type, and every interface resolves to that instance through a factory
    /// lambda — <see cref="IOutboxWriter"/>, <see cref="IOutboxProcessorStore"/>,
    /// <see cref="IOutboxAdminStore"/> and <see cref="IOutboxRetentionStore"/> for the outbox;
    /// <see cref="IInboxWriter"/>, <see cref="IInboxProcessorStore"/>,
    /// <see cref="IInboxSettlementStore"/>, <see cref="IInboxAdminStore"/> and
    /// <see cref="IInboxRetentionStore"/> for the inbox. One scope therefore holds one store
    /// over one <typeparamref name="TContext"/>.
    /// </para>
    /// <para>
    /// That is also what makes inbox settlement transactional. Resolved from the per-message
    /// execution scope, <see cref="IInboxSettlementStore"/> necessarily shares its
    /// <typeparamref name="TContext"/> with the handler resolved from the same scope, so the
    /// staged processed mark commits in the handler's own transaction. <b>Registering either
    /// store with any lifetime other than scoped breaks that guarantee silently.</b>
    /// </para>
    /// <para>
    /// <b>It also switches on the outbox and inbox engine</b> — the four hosted workers, the
    /// processors and the coordinators — through
    /// <see cref="OutboxInboxProcessingExtensions.AddOutboxInboxProcessing"/>. Since #186
    /// <c>AddMicroKitMessaging()</c> registers no worker — its only hosted service is the
    /// composition validator — so a host without this call runs no outbox or inbox worker at all. Transitional: #188 replaces it with opt-in durability on the bus.
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddEfCoreOutbox<TContext>(this MessagingBuilder builder)
        where TContext : DbContext
    {
        builder.Services.AddScoped<EfOutboxStore<TContext>>();
        builder.Services.AddScoped<IOutboxWriter>(
            sp => sp.GetRequiredService<EfOutboxStore<TContext>>());
        builder.Services.AddScoped<IOutboxProcessorStore>(
            sp => sp.GetRequiredService<EfOutboxStore<TContext>>());

        // Admin and retention are separate contracts (ISP) served by the same instance: a DLQ
        // console has no business seeing ClaimBatchAsync, and the processor has no business
        // seeing RequeueAsync.
        builder.Services.AddScoped<IOutboxAdminStore>(
            sp => sp.GetRequiredService<EfOutboxStore<TContext>>());
        builder.Services.AddScoped<IOutboxRetentionStore>(
            sp => sp.GetRequiredService<EfOutboxStore<TContext>>());
        // The inbox store follows the same shape as the outbox: the concrete type is registered
        // once as scoped, and each interface resolves to that single instance, so one scope holds
        // one store over one DbContext.
        //
        // That is also what makes settlement transactional, with no second registration needed.
        // IInboxSettlementStore is resolved from the PER-MESSAGE execution scope, which is a
        // fresh scope; the instance it yields therefore shares its TContext with the handler
        // resolved from that same scope, and the staged mark commits in the handler's own
        // transaction. Registering the store as anything other than scoped breaks that silently.
        builder.Services.AddScoped<EfInboxStore<TContext>>();
        builder.Services.AddScoped<IInboxWriter>(
            sp => sp.GetRequiredService<EfInboxStore<TContext>>());
        builder.Services.AddScoped<IInboxProcessorStore>(
            sp => sp.GetRequiredService<EfInboxStore<TContext>>());
        builder.Services.AddScoped<IInboxSettlementStore>(
            sp => sp.GetRequiredService<EfInboxStore<TContext>>());
        builder.Services.AddScoped<IInboxAdminStore>(
            sp => sp.GetRequiredService<EfInboxStore<TContext>>());
        builder.Services.AddScoped<IInboxRetentionStore>(
            sp => sp.GetRequiredService<EfInboxStore<TContext>>());

        // The workers require the stores registered above, so the store provider is what
        // registers them (#186) — not AddMicroKitMessaging().
        builder.AddOutboxInboxProcessing();

        return builder;
    }

    /// <summary>
    /// Registers integration-event publishing through the outbox: the EF Core staging writer and
    /// the <see cref="IIntegrationEventPublisher"/> that writes through it.
    /// </summary>
    /// <typeparam name="TContext">
    /// The application's <see cref="DbContext"/> type. Must have
    /// <see cref="ModelBuilderExtensions.ApplyMessagingConfiguration"/> called in
    /// <c>OnModelCreating</c>.
    /// </typeparam>
    /// <param name="builder">The <see cref="MessagingBuilder"/> handed to
    /// <c>AddMicroKitMessaging(bus => ...)</c>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>The publisher is registered here (#187)</b>, not by <c>AddMicroKitMessaging</c>: it
    /// cannot be activated without the <see cref="IIntegrationEventWriter"/> this call provides.
    /// The types it publishes are declared with <c>bus.Publishes&lt;T&gt;()</c>, like any
    /// publication. The publisher is scoped: it is resolved from the per-message execution scope,
    /// and a singleton would capture one execution context and one writer.
    /// </para>
    /// <para>
    /// <b>Scoped, and it is load-bearing.</b> The writer's <c>HasOpenTransaction</c> guard reads
    /// the transaction on the <typeparamref name="TContext"/> instance it holds, so the writer and
    /// the caller's unit of work must be the same instance — which is exactly what one scoped
    /// registration over one scoped <c>DbContext</c> gives. Registering it with any other lifetime
    /// makes the guard pass while the row commits somewhere else.
    /// </para>
    /// <para>
    /// <b>The provider must support savepoints.</b> The writer flushes inside the caller's
    /// transaction so a replayed publication is absorbed before <c>PublishAsync</c> returns, and it
    /// takes a savepoint first because a rejected insert aborts the whole transaction on PostgreSQL
    /// — without one, absorbing the replay would destroy the caller's own pending writes. A provider
    /// that cannot take a savepoint is refused at publish time with
    /// <c>IntegrationEventPublishException</c> rather than silently losing that protection. Both
    /// supported providers, PostgreSQL and SQLite, qualify.
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddEfCoreIntegrationEvents<TContext>(this MessagingBuilder builder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<IIntegrationEventWriter, EfIntegrationEventWriter<TContext>>();
        builder.Services.TryAddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();
        return builder;
    }
}
