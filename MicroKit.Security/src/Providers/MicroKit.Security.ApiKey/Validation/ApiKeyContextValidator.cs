using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Extraction;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Abstractions.Validation;
using MicroKit.Security.Abstractions.Validator;
using Microsoft.Extensions.Logging;

namespace MicroKit.Security.ApiKey.Validation;

/// <summary>Secondary validator that checks API key ownership against the already-authenticated primary principal to prevent cross-tenant shadowing.</summary>
public sealed class ApiKeyContextValidator(
    IApiKeyValidator internalValidator,
    ILogger<ApiKeyContextValidator> logger) : ISecurityValidator
{
    /// <inheritdoc/>
    public AuthenticationScheme TargetScheme => AuthenticationScheme.ApiKey;

    /// <inheritdoc/>
    public async ValueTask<ApiKeyValidationResult> ValidateAsync(
        ISecurityPrincipal primaryPrincipal,
        ExtractionResult secondaryCredential,
        CancellationToken ct)
    {
        var result = await internalValidator.ValidateAsync(
            secondaryCredential.Value.AsSpan(),
            ct);

        if (!result.IsValid)
        {
            logger.LogWarning("API key validation failed. Status: {Status}, Reason: {Error}",
                result.Status, result.ErrorMessage);
            return result;
        }

        var primaryTenant = primaryPrincipal.TenantId;
        var keyTenant = result.Principal!.TenantId;

        if (primaryTenant != keyTenant)
        {
            // Security invariant: block cross-tenant API key usage attempts
            logger.LogCritical(
                "Security violation: identity {UserId} (tenant: {PrimaryTenant}) attempted to use an API key belonging to tenant {KeyTenant}.",
                primaryPrincipal.Identifier, primaryTenant, keyTenant);

            return result;
        }

        logger.LogDebug("Context validation succeeded for user {UserId} on tenant {TenantId}",
            primaryPrincipal.Identifier, primaryTenant);

        return result;
    }
}
