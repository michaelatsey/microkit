namespace MicroKit.Multitenancy.EntityFrameworkCore;

using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Fluent builder for configuring EF Core tenant isolation.
/// Returned by <see cref="MultitenancyBuilderEfExtensions.AddEntityFrameworkCoreIsolation"/>.
/// </summary>
public sealed class EfIsolationBuilder(IServiceCollection services)
{
    /// <summary>The underlying service collection.</summary>
    public IServiceCollection Services => services;

    /// <summary>
    /// Configures row-level tenant isolation using a <c>TenantId</c> discriminator column.
    /// All tenants share the same tables; global query filters and the
    /// <see cref="TenantStampInterceptor"/> enforce isolation automatically.
    /// This is the Phase 1 isolation mode.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public EfIsolationBuilder UseSharedDatabase()
    {
        // Shared is the default — no additional services needed beyond TenantStampInterceptor.
        return this;
    }

    /// <summary>
    /// Configures schema-per-tenant isolation where each tenant has its own database schema.
    /// </summary>
    /// <remarks>
    /// <b>Phase 2 — not yet implemented.</b> Calling this method throws immediately at DI
    /// registration time (fail-fast) to prevent silent mis-configuration in production.
    /// Use <see cref="UseSharedDatabase"/> for Phase 1 isolation.
    /// </remarks>
    /// <exception cref="NotSupportedException">Always thrown — Phase 2 is not implemented.</exception>
    [Experimental("MKT_EF_PHASE2", UrlFormat = "https://github.com/michaelatsey/microkit/issues")]
    public EfIsolationBuilder UseSchemaIsolation()
    {
        // A1: Fail-fast at DI registration (startup), not at query time.
        throw new NotSupportedException(
            "Schema isolation mode (MKT_EF_PHASE2) is planned for Phase 2 and is not yet implemented. " +
            "Use UseSharedDatabase() for Phase 1 row-level tenant isolation.");
    }

    /// <summary>
    /// Configures database-per-tenant isolation where each tenant has its own connection string
    /// from <see cref="ITenantInfo.ConnectionString"/>.
    /// </summary>
    /// <remarks>
    /// <b>Phase 2 — not yet implemented.</b> Calling this method throws immediately at DI
    /// registration time (fail-fast) to prevent silent mis-configuration in production.
    /// Use <see cref="UseSharedDatabase"/> for Phase 1 isolation.
    /// </remarks>
    /// <exception cref="NotSupportedException">Always thrown — Phase 2 is not implemented.</exception>
    [Experimental("MKT_EF_PHASE2", UrlFormat = "https://github.com/michaelatsey/microkit/issues")]
    public EfIsolationBuilder UseDatabaseIsolation()
    {
        // A1: Fail-fast at DI registration (startup), not at query time.
        throw new NotSupportedException(
            "Database isolation mode (MKT_EF_PHASE2) is planned for Phase 2 and is not yet implemented. " +
            "Use UseSharedDatabase() for Phase 1 row-level tenant isolation.");
    }

    /// <summary>
    /// Registers <see cref="EfTenantStore"/> as the active <see cref="ITenantStore"/>,
    /// backed by a <see cref="TenantStoreDbContext"/> configured by <paramref name="configureOptions"/>.
    /// </summary>
    /// <param name="configureOptions">
    /// Callback to configure the <see cref="TenantStoreDbContext"/> (connection string, provider, etc.).
    /// </param>
    /// <returns>This builder for chaining.</returns>
    public EfIsolationBuilder UseEfStore(Action<DbContextOptionsBuilder> configureOptions)
    {
        services.AddDbContext<TenantStoreDbContext>(configureOptions);
        services.AddScoped<ITenantStore, EfTenantStore>();
        return this;
    }
}
