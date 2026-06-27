namespace MicroKit.Tenancy;

/// <summary>
/// Raised by <see cref="ITenantResolver"/> when no registered
/// <see cref="ITenantResolutionStrategy"/> could identify the current tenant.
/// </summary>
public sealed record ResolutionFailedError()
    : Error(ErrorCode.From("MULTITENANCY.RESOLUTION.FAILED"), "No strategy could resolve the current tenant.")
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.NotFound;
}
