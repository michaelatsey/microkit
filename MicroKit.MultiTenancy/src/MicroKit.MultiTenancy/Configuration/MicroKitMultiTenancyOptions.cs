namespace MicroKit.MultiTenancy.Configuration;

/// <summary>Configuration options for the MicroKit multi-tenancy module.</summary>
public class MicroKitMultiTenancyOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "MicroKit:MultiTenancy:Core";

    /// <summary>Gets or sets the HTTP header used to identify the tenant.</summary>
    public string HeaderName { get; set; } = "X-Tenant-Id";
    /// <summary>Gets or sets the JWT claim name carrying the tenant identifier.</summary>
    public string ClaimNames { get; set; } = "tenant_id";

    /// <summary>Gets or sets URL path prefixes that bypass tenant resolution.</summary>
    public HashSet<string> ExemptedPaths { get; set; } = [];
    /// <summary>Gets or sets whether the module-validation hosted service runs on startup.</summary>
    public bool EnableValidationWorker { get; set; } = true;
}
