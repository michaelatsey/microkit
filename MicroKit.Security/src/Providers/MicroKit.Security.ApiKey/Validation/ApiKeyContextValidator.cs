using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Extraction;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Abstractions.Validation;
using MicroKit.Security.Abstractions.Validator;
using Microsoft.Extensions.Logging;

namespace MicroKit.Security.ApiKey.Validation;

public sealed class ApiKeyContextValidator(
    IApiKeyValidator internalValidator,
    ILogger<ApiKeyContextValidator> logger) : ISecurityValidator
{
    public AuthenticationScheme TargetScheme => AuthenticationScheme.ApiKey;

    public async ValueTask<ApiKeyValidationResult> ValidateAsync(
        ISecurityPrincipal primaryPrincipal,
        ExtractionResult secondaryCredential,
        CancellationToken ct)
    {
        // 1. Validation technique HAUTE PERFORMANCE
        // On convertit la string en ReadOnlySpan<char> pour éviter les allocations inutiles
        // dans le moteur de validation interne (IApiKeyValidator).
        var result = await internalValidator.ValidateAsync(
            secondaryCredential.Value.AsSpan(),
            ct);

        // 2. Gestion granulaire des échecs (Logging intelligent)
        if (!result.IsValid)
        {
            logger.LogWarning("Validation API Key échouée. Statut: {Status}, Raison: {Error}",
                result.Status, result.ErrorMessage);
            return result;
        }

        // 3. LOGIQUE DE COHÉRENCE CONTEXTUELLE (Anti-Shadowing)
        // Comparaison des tenants pour s'assurer que l'identité primaire (JWT) 
        // et le signal secondaire (API Key) appartiennent au même silo.
        var primaryTenant = primaryPrincipal.TenantId;
        var keyTenant = result.Principal!.TenantId;

        if (primaryTenant != keyTenant)
        {
            // CRITICAL : C'est ici qu'on bloque une tentative potentielle d'escalade 
            // ou de confusion de contexte entre deux clients différents.
            logger.LogCritical(
                "CONFLIT DE SÉCURITÉ : L'identité {UserId} (Tenant: {PrimaryT}) tente d'utiliser " +
                "une clé API appartenant au Tenant {SecondaryT}.",
                primaryPrincipal.Identifier, primaryTenant, keyTenant);

            return result;
        }

        logger.LogDebug("Validation contextuelle réussie pour l'utilisateur {UserId} sur le Tenant {TenantId}",
            primaryPrincipal.Identifier, primaryTenant);

        return result;
    }
}