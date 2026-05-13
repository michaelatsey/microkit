using MicroKit.MultiTenancy.Abstractions;
using MicroKit.Security.Abstractions.Contexts;

namespace MicroKit.Security.MultiTenancy;

/// <summary>
/// Resolves the current tenant by reading the <see cref="IClientContext.TenantId"/> from the authenticated
/// security principal. Registers as an <see cref="ITenantResolutionStrategy"/> so that
/// <c>MicroKit.MultiTenancy</c> tenant resolution pipelines can source the tenant from the JWT or API key claim.
/// </summary>
public sealed class SecurityPrincipalTenantResolutionStrategy(
    IClientContextAccessor contextAccessor) : ITenantResolutionStrategy
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(contextAccessor.Context?.TenantId);
}
