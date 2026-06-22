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
    /// The EF Core outbox store is registered once as scoped and resolved via factory lambdas
    /// for both <see cref="IOutboxWriter"/> and <see cref="IOutboxProcessorStore"/> —
    /// guaranteeing a single <typeparamref name="TContext"/> instance is shared within
    /// the scope (no double-instantiation).
    /// </remarks>
    public static MessagingBuilder AddEfCoreOutbox<TContext>(this MessagingBuilder builder)
        where TContext : DbContext
    {
        builder.Services.AddScoped<EfOutboxStore<TContext>>();
        builder.Services.AddScoped<IOutboxWriter>(
            sp => sp.GetRequiredService<EfOutboxStore<TContext>>());
        builder.Services.AddScoped<IOutboxProcessorStore>(
            sp => sp.GetRequiredService<EfOutboxStore<TContext>>());
        builder.Services.AddScoped<IInboxStore, EfInboxStore<TContext>>();
        return builder;
    }
}
