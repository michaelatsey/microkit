namespace MicroKit.Tenancy;

/// <summary>Raised when a tenant cannot be found in the <see cref="ITenantStore"/>.</summary>
public sealed record TenantNotFoundError()
    : Error(ErrorCode.From("MULTITENANCY.TENANT.NOT_FOUND"), "Tenant not found.")
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.NotFound;
}
