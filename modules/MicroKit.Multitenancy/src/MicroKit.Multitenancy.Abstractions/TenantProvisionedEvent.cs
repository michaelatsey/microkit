namespace MicroKit.Multitenancy;

/// <summary>
/// Domain event raised when a new tenant has been successfully provisioned.
/// </summary>
/// <param name="TenantId">The identifier assigned to the new tenant.</param>
/// <param name="Name">The tenant display name.</param>
/// <param name="ProvisionedAt">The UTC timestamp when provisioning completed.</param>
public sealed record TenantProvisionedEvent(
    TenantId TenantId,
    string Name,
    DateTimeOffset ProvisionedAt);
