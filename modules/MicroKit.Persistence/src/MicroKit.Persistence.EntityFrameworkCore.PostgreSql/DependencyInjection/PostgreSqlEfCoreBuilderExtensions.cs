namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql;

/// <summary>
/// PostgreSQL (Npgsql) provider extension for <see cref="EfCoreBuilder"/>.
/// </summary>
public static class PostgreSqlEfCoreBuilderExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> as a scoped <see cref="DbContext"/> using the
    /// Npgsql provider with the given <paramref name="connectionString"/>.
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> type.</typeparam>
    /// <param name="builder">The EF Core builder.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="npgsqlOptions">
    ///   Optional delegate to further configure Npgsql-specific options such as
    ///   <c>EnableRetryOnFailure</c>, command timeout, or schema name.
    /// </param>
    /// <returns><paramref name="builder"/> for fluent chaining.</returns>
    public static EfCoreBuilder UsePostgreSQL<TContext>(
        this EfCoreBuilder builder,
        string connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptions = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        builder.Services.AddDbContext<TContext>(opts =>
            opts.UseNpgsql(connectionString, npgsqlOptions));
        return builder;
    }
}
