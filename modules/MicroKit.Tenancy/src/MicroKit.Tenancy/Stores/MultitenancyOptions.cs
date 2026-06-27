namespace MicroKit.Tenancy;

/// <summary>
/// Configuration options for MicroKit.Tenancy.
/// Bind via: <c>services.Configure&lt;MultitenancyOptions&gt;(config.GetSection(<see cref="SectionKey"/>))</c>
/// </summary>
public sealed class MultitenancyOptions
{
    /// <summary>Default configuration section key (<c>"Multitenancy"</c>).</summary>
    public const string SectionKey = "Multitenancy";

    /// <summary>
    /// Tenant definitions loaded from configuration.
    /// Consumed by <see cref="ConfigurationTenantStore"/>.
    /// </summary>
    public List<TenantRecord> Tenants { get; set; } = [];
}
