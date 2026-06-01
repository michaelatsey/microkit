namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Extension methods for registering MicroKit.Persistence services on
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Adds MicroKit.Persistence services to <paramref name="services"/> and returns a
    /// <see cref="PersistenceBuilder"/> for further configuration.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configure">Optional delegate to configure persistence services.</param>
    /// <returns><paramref name="services"/> for fluent chaining.</returns>
    public static IServiceCollection AddMicroKitPersistence(
        this IServiceCollection services,
        Action<PersistenceBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var builder = new PersistenceBuilder(services);
        configure?.Invoke(builder);
        return services;
    }

    /// <summary>
    /// Adds EF Core persistence services and returns an <see cref="EfCoreBuilder"/> for
    /// further configuration. Registers <see cref="EfSpecificationEvaluator"/> as a singleton
    /// <see cref="ISpecificationEvaluator"/> (idempotent — safe to call multiple times).
    /// </summary>
    /// <param name="builder">The parent persistence builder.</param>
    /// <param name="configure">Optional delegate to configure EF Core services.</param>
    /// <returns>An <see cref="EfCoreBuilder"/> for chaining provider registrations.</returns>
    public static EfCoreBuilder AddEntityFrameworkCore(
        this PersistenceBuilder builder,
        Action<EfCoreBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<ISpecificationEvaluator, EfSpecificationEvaluator>();
        var efBuilder = new EfCoreBuilder(builder);
        configure?.Invoke(efBuilder);
        return efBuilder;
    }

    /// <summary>
    /// Registers a <typeparamref name="TContext"/> <see cref="DbContext"/> in the service
    /// collection. Delegates to <see cref="EntityFrameworkServiceCollectionExtensions.AddDbContext{TContext}(IServiceCollection, Action{DbContextOptionsBuilder}?, ServiceLifetime, ServiceLifetime)"/>.
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> type.</typeparam>
    /// <param name="builder">The EF Core builder.</param>
    /// <param name="configure">Optional delegate to configure <see cref="DbContextOptionsBuilder"/>.</param>
    /// <returns><paramref name="builder"/> for fluent chaining.</returns>
    public static EfCoreBuilder AddDbContext<TContext>(
        this EfCoreBuilder builder,
        Action<DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddDbContext<TContext>(configure);
        return builder;
    }

    /// <summary>
    /// Registers <see cref="EfUnitOfWork{TContext}"/> as a scoped service and binds it to
    /// <see cref="IUnitOfWork"/>, <see cref="ITransactionalContext"/>, and
    /// <see cref="ITransactionalUnitOfWork"/> — one instance, three interface pointers.
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> type.</typeparam>
    /// <param name="builder">The EF Core builder.</param>
    /// <returns><paramref name="builder"/> for fluent chaining.</returns>
    /// <remarks>
    /// Handlers inject the narrowest interface they need:
    /// command handlers use <see cref="IUnitOfWork"/>;
    /// <c>TransactionBehavior</c> uses <see cref="ITransactionalContext"/>.
    /// </remarks>
    public static EfCoreBuilder AddUnitOfWork<TContext>(this EfCoreBuilder builder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddScoped<EfUnitOfWork<TContext>>();
        builder.Services.AddScoped<IUnitOfWork>(
            sp => sp.GetRequiredService<EfUnitOfWork<TContext>>());
        builder.Services.AddScoped<ITransactionalContext>(
            sp => sp.GetRequiredService<EfUnitOfWork<TContext>>());
        builder.Services.AddScoped<ITransactionalUnitOfWork>(
            sp => sp.GetRequiredService<EfUnitOfWork<TContext>>());
        return builder;
    }
}
