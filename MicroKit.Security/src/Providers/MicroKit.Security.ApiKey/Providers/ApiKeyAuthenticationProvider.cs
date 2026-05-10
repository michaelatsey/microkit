
using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Validation;
using MicroKit.Security.Core.Providers;
using Microsoft.Extensions.Logging;

namespace MicroKit.Security.ApiKey.Providers;
public sealed class ApiKeyAuthenticationProvider(
    IApiKeyValidator validator,
    ILogger<ApiKeyAuthenticationProvider> logger) : IAuthenticationProvider
{

    public AuthenticationScheme Scheme => AuthenticationScheme.ApiKey;

    public async ValueTask<AuthenticationResult> AuthenticateAsync(
        ReadOnlyMemory<char> credentials,
        CancellationToken cancellationToken = default)
    {
        // 1. Délégation immédiate au Validateur (Haute Performance via Span)
        // Note: On utilise le Span ici car AuthenticateAsync n'est pas suspendu 
        // AVANT l'appel au validateur.
        var validationResult = await validator.ValidateAsync(credentials.Span, cancellationToken);

        if (!validationResult.IsValid)
        {
            logger.LogWarning("Authentification par API Key échouée: {Status}", validationResult.Status);
            return validationResult.Status switch
            {
                ValidationStatus.Expired => AuthenticationResult.Expired(validationResult.ErrorMessage),
                ValidationStatus.Revoked => AuthenticationResult.Revoked(validationResult.ErrorMessage),
                _ => AuthenticationResult.Invalid(validationResult.ErrorMessage ?? "Invalid API Key")
            };
        }

        // 3. Succès : On récupère le Principal et les métadonnées
        // On extrait l'expiration des métadonnées si présente
        DateTimeOffset? expiresAt = null;
        if (validationResult.Metadata?.TryGetValue("expires_at", out var exp) == true)
        {
            expiresAt = (DateTimeOffset)exp;
        }

        return AuthenticationResult.Success(
             validationResult.Principal!,
             expiresAt,
             validationResult.Metadata);
    }
}