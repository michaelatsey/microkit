namespace MicroKit.Tenancy;

/// <summary>
/// Read-only view of the current tenant context for the active async execution flow.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The current tenant, or <see langword="null"/> if no tenant has been resolved.
    /// </summary>
    ITenantInfo? CurrentTenant { get; }
}
