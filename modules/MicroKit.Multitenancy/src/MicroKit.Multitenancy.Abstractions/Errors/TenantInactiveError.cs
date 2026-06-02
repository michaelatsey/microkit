namespace MicroKit.Multitenancy;

/// <summary>Raised when the resolved tenant is inactive and cannot process requests.</summary>
public sealed record TenantInactiveError()
    : Error(ErrorCode.From("MULTITENANCY.TENANT.INACTIVE"), "The tenant is inactive.")
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.Forbidden;
}
