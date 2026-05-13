using MicroKit.Security.Abstractions.Contexts;
using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace MicroKit.Security.Core.Contexts;

/// <summary>
/// Default <see cref="ISecurityContextFactory"/> implementation.
/// Validates tenant consistency between JWT identity and request headers.
/// </summary>
public sealed class SecurityContextFactory(
    TimeProvider timeProvider,
    ILogger<SecurityContextFactory> logger) : ISecurityContextFactory
{
    /// <inheritdoc />
    public IClientContext CreateContext(
        ISecurityPrincipal principal,
        AuthenticationScheme scheme,
        string? tenantId = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, object>? metadata = null)
        => new ClientContext(
            CorrelationId: correlationId ?? Guid.NewGuid().ToString("N"),
            Principal: principal,
            Scheme: scheme,
            TenantId: ResolveAndValidateTenant(principal.TenantId, tenantId),
            CreatedAt: timeProvider.GetUtcNow(),
            Metadata: metadata ?? new Dictionary<string, object>());

    private string? ResolveAndValidateTenant(string? identityTenantId, string? headerTenantId)
    {
        // Identity tenant (from JWT) is sovereign
        if (!string.IsNullOrEmpty(identityTenantId))
        {
            // Guard against header-based tenant shadowing attacks
            if (!string.IsNullOrEmpty(headerTenantId) &&
                !headerTenantId.Equals(identityTenantId, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogCritical(
                    "Security violation: Tenant mismatch. Identity: {IdTenant}, Header: {HeaderTenant}",
                    identityTenantId, headerTenantId);

                throw new InvalidOperationException("Security violation: Tenant mismatch.");
            }

            return identityTenantId;
        }

        // For global identities, fall back to header-supplied tenant
        return headerTenantId;
    }
}
