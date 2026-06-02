namespace MicroKit.Multitenancy;

/// <summary>Raised when a tenant identifier has an invalid or unparseable format.</summary>
public sealed record InvalidTenantIdError()
    : Error(ErrorCode.From("MULTITENANCY.TENANT.INVALID_ID"), "The provided tenant identifier is invalid.")
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.Validation;
}
