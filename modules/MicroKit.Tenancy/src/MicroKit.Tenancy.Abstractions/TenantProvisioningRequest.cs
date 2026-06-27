namespace MicroKit.Tenancy;

/// <summary>Parameters for provisioning a new tenant.</summary>
/// <param name="Name">The tenant display name. Must be unique across the system.</param>
public sealed record TenantProvisioningRequest(string Name)
{
    /// <summary>Optional connection string for database-per-tenant isolation.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>Optional schema name for schema-per-tenant isolation.</summary>
    public string? SchemaName { get; init; }
}
