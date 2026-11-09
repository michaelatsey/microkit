using System.Diagnostics.CodeAnalysis;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql;

/// <summary>
/// PostgreSQL (Npgsql) provider extension for <see cref="EfCoreBuilder"/>.
/// </summary>
public static class PostgreSqlEfCoreBuilderExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> as a scoped <see cref="DbContext"/>, with
    /// <c>UseMicroKitPersistence()</c>, using the
    /// Npgsql provider with the given <paramref name="connectionString"/>, leased from a context pool
    /// when <see cref="ProviderOptions.PoolSize"/> is set.
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> type.</typeparam>
    /// <param name="builder">The EF Core builder.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="npgsqlOptions">
    ///   Optional delegate to further configure Npgsql-specific options such as command timeout or
    ///   schema name. It runs after <paramref name="options"/> is applied, so it can override it.
    /// </param>
    /// <param name="options">
    ///   Opt-in transient-failure retry and context pooling; both are off when <see langword="null"/>.
    /// </param>
    /// <returns><paramref name="builder"/> for fluent chaining.</returns>
    /// <exception cref="ArgumentException">
    ///   <paramref name="connectionString"/> is empty or white space; or
    ///   <see cref="ProviderOptions.PoolSize"/> is set and <typeparamref name="TContext"/>'s only public
    ///   constructor does not take just <c>DbContextOptions</c> or <c>DbContextOptions&lt;TContext&gt;</c>.
    /// </exception>
    public static EfCoreBuilder UsePostgreSQL<TContext>(
        this EfCoreBuilder builder,
        string connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptions = null,
        ProviderOptions? options = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var retry = options?.Retry;
        void Configure(DbContextOptionsBuilder opts) =>
            opts.UseNpgsql(connectionString, npgsql =>
            {
                if (retry is not null)
                    npgsql.EnableRetryOnFailure(retry.MaxRetryCount, retry.MaxRetryDelay, errorCodesToAdd: null);
                npgsqlOptions?.Invoke(npgsql);
            }).UseMicroKitPersistence();

        if (options?.PoolSize is { } poolSize)
        {
            EnsurePoolable<TContext>(nameof(options));
            builder.Services.AddDbContextPool<TContext>(Configure, poolSize);
        }
        else
        {
            builder.Services.AddDbContext<TContext>(Configure);
        }

        return builder;
    }

    // With PoolSize set, refuse a context EF Core would pool unsafely. Kept private to each provider
    // package: a shared internal API would bind two separately versioned packages.
    private static void EnsurePoolable<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TContext>(
        string paramName)
        where TContext : DbContext
    {
        if (typeof(TContext).GetConstructors() is [var only]
            && only.GetParameters() is [var parameter]
            && (parameter.ParameterType == typeof(DbContextOptions)
                || parameter.ParameterType == typeof(DbContextOptions<TContext>)))
        {
            return;
        }

        throw new ArgumentException(
            $"'{typeof(TContext).Name}' cannot be pooled: with PoolSize set, its only public constructor must take "
            + $"just DbContextOptions or DbContextOptions<{typeof(TContext).Name}>. A pooled context's "
            + "constructor-injected scoped services are resolved once from the root provider and shared by every "
            + "lease, so tenant-aware contexts such as MultitenantDbContext must not be pooled. Remove PoolSize, or "
            + "give the context a single options-only constructor.",
            paramName);
    }
}
