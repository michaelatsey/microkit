namespace MicroKit.Persistence.EntityFrameworkCore.SqlServer;

/// <summary>
/// SQL Server provider extension for <see cref="EfCoreBuilder"/>.
/// </summary>
public static class SqlServerEfCoreBuilderExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> as a scoped <see cref="DbContext"/> using the
    /// SQL Server provider with the given <paramref name="connectionString"/>.
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> type.</typeparam>
    /// <param name="builder">The EF Core builder.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="sqlServerOptions">
    ///   Optional delegate to further configure SQL Server-specific options such as
    ///   <c>EnableRetryOnFailure</c>, command timeout, or Azure AD auth.
    /// </param>
    /// <returns><paramref name="builder"/> for fluent chaining.</returns>
    public static EfCoreBuilder UseSqlServer<TContext>(
        this EfCoreBuilder builder,
        string connectionString,
        Action<SqlServerDbContextOptionsBuilder>? sqlServerOptions = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        builder.Services.AddDbContext<TContext>(opts =>
            opts.UseSqlServer(connectionString, sqlServerOptions));
        return builder;
    }
}
