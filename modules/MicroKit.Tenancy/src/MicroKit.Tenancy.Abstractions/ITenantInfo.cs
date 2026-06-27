namespace MicroKit.Tenancy;

/// <summary>Describes a registered tenant and its configuration.</summary>
public interface ITenantInfo
{
    /// <summary>Unique tenant identifier.</summary>
    TenantId Id { get; }

    /// <summary>Human-readable tenant name.</summary>
    string Name { get; }

    /// <summary>Optional connection string override for database-per-tenant isolation.</summary>
    string? ConnectionString { get; }

    /// <summary>Optional schema name override for schema-per-tenant isolation.</summary>
    string? SchemaName { get; }

    /// <summary>Whether this tenant is active and may process requests.</summary>
    bool IsActive { get; }
}
