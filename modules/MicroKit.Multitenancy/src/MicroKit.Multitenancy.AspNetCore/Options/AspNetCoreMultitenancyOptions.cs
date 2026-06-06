namespace MicroKit.Multitenancy.AspNetCore;

/// <summary>
/// Configuration options for MicroKit.Multitenancy.AspNetCore HTTP resolution strategies.
/// Pass to <see cref="AspNetCoreMultitenancyBuilderExtensions.AddAspNetCoreResolution"/>.
/// </summary>
public sealed class AspNetCoreMultitenancyOptions
{
    /// <summary>
    /// HTTP header name containing the tenant identifier.
    /// Used by <see cref="HeaderTenantResolutionStrategy"/>. Default: <c>X-Tenant-Id</c>.
    /// </summary>
    public string HeaderName { get; set; } = "X-Tenant-Id";

    /// <summary>
    /// Route parameter name containing the tenant identifier.
    /// Used by <see cref="RouteDataTenantResolutionStrategy"/>. Default: <c>tenantId</c>.
    /// </summary>
    public string RouteParameterName { get; set; } = "tenantId";

    /// <summary>
    /// Zero-based index of the subdomain segment to use as the tenant identifier.
    /// For <c>acme.app.example.com</c>, index <c>0</c> yields <c>acme</c>.
    /// Used by <see cref="SubdomainTenantResolutionStrategy"/>. Default: <c>0</c>.
    /// </summary>
    public int SubdomainSegmentIndex { get; set; }

    /// <summary>
    /// JWT or cookie claim type containing the tenant identifier.
    /// Used by <see cref="ClaimsTenantResolutionStrategy"/>. Default: <c>tenant_id</c>.
    /// </summary>
    public string ClaimType { get; set; } = "tenant_id";

    /// <summary>
    /// Maps full host names to tenant identifiers (case-insensitive).
    /// Used by <see cref="HostTenantResolutionStrategy"/>.
    /// </summary>
    public IDictionary<string, TenantId> HostMappings { get; set; } =
        new Dictionary<string, TenantId>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers <see cref="SubdomainTenantResolutionStrategy"/> (Order: 30).
    /// Off by default — requires Guid-formatted subdomain segments (e.g., <c>{guid}.app.example.com</c>).
    /// Slug-based subdomain resolution is deferred to Phase 2.
    /// </summary>
    public bool EnableSubdomain { get; set; }

    /// <summary>
    /// Registers <see cref="HostTenantResolutionStrategy"/> (Order: 50).
    /// Off by default — requires <see cref="HostMappings"/> to be populated.
    /// </summary>
    public bool EnableHost { get; set; }
}
