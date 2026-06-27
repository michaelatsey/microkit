namespace MicroKit.Tenancy.EntityFrameworkCore;

/// <summary>
/// Extension methods on <see cref="MultitenancyBuilder"/> for registering EF Core tenant isolation.
/// </summary>
public static class MultitenancyBuilderEfExtensions
{
    /// <summary>
    /// Registers EF Core tenant isolation services:
    /// <list type="bullet">
    /// <item><see cref="TenantStampInterceptor"/> (Scoped) — stamps <c>TenantId</c> on <c>Added</c> entities.</item>
    /// </list>
    /// Use the returned <see cref="EfIsolationBuilder"/> to select an isolation mode and
    /// optionally register a database-backed <see cref="ITenantStore"/> via
    /// <see cref="EfIsolationBuilder.UseEfStore"/>.
    /// </summary>
    /// <param name="builder">The multitenancy builder.</param>
    /// <param name="configure">Optional callback for additional EF isolation configuration.</param>
    /// <returns>The same <paramref name="builder"/> for chaining with other multitenancy extensions.</returns>
    /// <example>
    /// <code>
    /// services.AddMicroKitMultitenancy(mt =>
    ///     mt.AddEntityFrameworkCoreIsolation(ef => ef
    ///         .UseSharedDatabase()
    ///         .UseEfStore(opts => opts.UseSqlServer(connectionString))));
    ///
    /// // In AddDbContext, wire the interceptor:
    /// services.AddDbContext&lt;AppDbContext&gt;((sp, opts) =>
    /// {
    ///     opts.UseSqlServer(connectionString);
    ///     opts.AddTenantStamping(sp);
    /// });
    /// </code>
    /// </example>
    public static MultitenancyBuilder AddEntityFrameworkCoreIsolation(
        this MultitenancyBuilder builder,
        Action<EfIsolationBuilder>? configure = null)
    {
        builder.Services.AddScoped<TenantStampInterceptor>();

        var efBuilder = new EfIsolationBuilder(builder.Services);
        configure?.Invoke(efBuilder);

        return builder;
    }
}
