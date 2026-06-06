namespace MicroKit.Multitenancy.EntityFrameworkCore;

/// <summary>
/// EF Core entity representing a registered tenant in the persistent store.
/// </summary>
/// <remarks>
/// <b>This type does NOT implement <see cref="ITenantEntity"/>.</b>
/// Tenant records are cross-tenant data (the registry itself) and must be globally visible
/// without a tenant query filter. Configuring this as a tenant-scoped entity would create
/// a bootstrapping paradox — the store cannot find tenants if the lookup is filtered by tenant.
/// </remarks>
public sealed class EfTenantRecord
{
    /// <summary>Unique tenant identifier (primary key — stored as UUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Human-readable tenant name. Maximum 256 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional connection string for database-per-tenant isolation (Phase 2).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Optional schema name for schema-per-tenant isolation (Phase 2).</summary>
    public string? SchemaName { get; set; }

    /// <summary>Whether this tenant is active and may process requests.</summary>
    public bool IsActive { get; set; }

    /// <summary>Adapts this record to the <see cref="ITenantInfo"/> contract.</summary>
    internal ITenantInfo ToTenantInfo() => new TenantInfoAdapter(this);

    private sealed class TenantInfoAdapter(EfTenantRecord record) : ITenantInfo
    {
        public TenantId Id { get; } = new TenantId(record.Id);
        public string Name { get; } = record.Name;
        public string? ConnectionString { get; } = record.ConnectionString;
        public string? SchemaName { get; } = record.SchemaName;
        public bool IsActive { get; } = record.IsActive;
    }
}
