namespace MicroKit.Messaging.EntityFrameworkCore;

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
    /// <param name="builder">The <see cref="MessagingBuilder"/> returned by
    /// <c>AddMicroKitMessaging()</c>.</param>
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
        return builder;
    }
}
