using MicroKit.Security.Abstractions.Contexts;
using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Core.Authentication;
using Microsoft.Extensions.Logging;

namespace MicroKit.Security.Core.Contexts;
/// <summary>
/// Core security service implementation.
/// </summary>
public sealed class SecurityContextFactory(
    TimeProvider timeProvider,
    ILogger<AuthenticationService> logger) : ISecurityContextFactory
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
                Metadata: metadata ?? new Dictionary<string, object>()
                );
    

    private string? ResolveAndValidateTenant(string? identityTenantId, string? headerTenantId)
    {
        // 1. Si l'identité possède un Tenant (ex: JWT), il est souverain
        if (!string.IsNullOrEmpty(identityTenantId))
        {
            // Vérification de cohérence avec le header si présent
            if (!string.IsNullOrEmpty(headerTenantId) &&
                !headerTenantId.Equals(identityTenantId, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogCritical("Security violation: Tenant mismatch. Identity: {IdTenant}, Header: {HeaderTenant}",
                    identityTenantId, headerTenantId);

                throw new InvalidOperationException("Security violation: Tenant mismatch.");
            }

            return identityTenantId;
        }

        // 2. Si l'identité est globale, on accepte le Tenant fourni par le contexte de requête (Header)
        return headerTenantId;
    }

}


