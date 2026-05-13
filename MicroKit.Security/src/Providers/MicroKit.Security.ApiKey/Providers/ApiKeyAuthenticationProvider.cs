using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Validation;
using MicroKit.Security.Core.Providers;
using Microsoft.Extensions.Logging;

namespace MicroKit.Security.ApiKey.Providers;

/// <summary>Authentication provider that validates API keys via <see cref="IApiKeyValidator"/>.</summary>
public sealed class ApiKeyAuthenticationProvider(
    IApiKeyValidator validator,
    ILogger<ApiKeyAuthenticationProvider> logger) : IAuthenticationProvider
{
    /// <inheritdoc/>
    public AuthenticationScheme Scheme => AuthenticationScheme.ApiKey;

    /// <inheritdoc/>
    public async ValueTask<AuthenticationResult> AuthenticateAsync(
        ReadOnlyMemory<char> credentials,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(credentials.Span, cancellationToken);

        if (!validationResult.IsValid)
        {
            logger.LogWarning("API key authentication failed: {Status}", validationResult.Status);
            return validationResult.Status switch
            {
                ValidationStatus.Expired => AuthenticationResult.Expired(validationResult.ErrorMessage),
                ValidationStatus.Revoked => AuthenticationResult.Revoked(validationResult.ErrorMessage),
                _ => AuthenticationResult.Invalid(validationResult.ErrorMessage ?? "Invalid API Key")
            };
        }

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
