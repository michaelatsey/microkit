namespace MicroKit.Multitenancy.EntityFrameworkCore;

/// <summary>
/// Minimal <see cref="DbContext"/> for the <see cref="EfTenantStore"/>.
/// Contains only the <c>Tenants</c> table — the cross-tenant registry.
/// </summary>
/// <remarks>
/// <para>
/// This context extends <see cref="DbContext"/> <b>directly</b> and must NEVER extend
/// <see cref="MultitenantDbContext"/>. Inheriting <see cref="MultitenantDbContext"/> would
/// apply tenant query filters to <see cref="EfTenantRecord"/> (the tenant registry table),
/// creating a bootstrapping paradox:
/// the store cannot look up a tenant without an active tenant context.
/// </para>
/// <para>
/// Consumers may subclass <see cref="TenantStoreDbContext"/> to co-locate the tenant table
/// inside their existing application database, as long as the above constraint is respected.
/// </para>
/// </remarks>
public class TenantStoreDbContext(DbContextOptions<TenantStoreDbContext> options) : DbContext(options)
{
    /// <summary>Gets or sets the tenants registered in the store.</summary>
    public DbSet<EfTenantRecord> Tenants { get; set; } = default!;

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new EfTenantRecordConfiguration());
    }
}
