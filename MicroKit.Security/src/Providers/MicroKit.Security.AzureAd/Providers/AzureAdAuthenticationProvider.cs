using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.AzureAd.Options;
using MicroKit.Security.Core.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace MicroKit.Security.AzureAd.Providers;

/// <summary>
/// Authentication provider that validates Azure Active Directory (Entra ID) tokens.
/// Keys are fetched via OIDC discovery and cached with automatic rotation support.
/// </summary>
public sealed class AzureAdAuthenticationProvider : IAuthenticationProvider
{
    private readonly AzureAdOptions _options;
    private readonly JsonWebTokenHandler _tokenHandler;
    private readonly TokenValidationParameters _validationParameters;
    private readonly ILogger<AzureAdAuthenticationProvider> _logger;

    /// <summary>Initializes a new instance and starts OIDC key discovery.</summary>
    /// <param name="options">Azure AD configuration.</param>
    /// <param name="logger">Logger.</param>
    public AzureAdAuthenticationProvider(
        IOptions<AzureAdOptions> options,
        ILogger<AzureAdAuthenticationProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _tokenHandler = new JsonWebTokenHandler();

        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            _options.MetadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true })
        {
            AutomaticRefreshInterval = TimeSpan.FromMinutes(_options.JwksKeyRefreshMinutes),
            RefreshInterval = TimeSpan.FromMinutes(5)
        };

        _validationParameters = BuildValidationParameters(configManager);
    }

    /// <inheritdoc />
    public AuthenticationScheme Scheme => AuthenticationScheme.AzureAd;

    /// <inheritdoc />
    public async ValueTask<AuthenticationResult> AuthenticateAsync(
        ReadOnlyMemory<char> credentials,
        CancellationToken cancellationToken = default)
    {
        if (credentials.IsEmpty)
            return AuthenticationResult.Invalid("Token is required");

        var token = credentials.ToString();

        try
        {
            var result = await _tokenHandler.ValidateTokenAsync(token, _validationParameters);

            if (!result.IsValid)
                return MapValidationFailure(result);

            return BuildSuccessResult(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private TokenValidationParameters BuildValidationParameters(
        IConfigurationManager<OpenIdConnectConfiguration> configManager)
    {
        var audience = _options.Audience ?? _options.ClientId;
        var audiences = new List<string> { audience };
        audiences.AddRange(_options.AdditionalAudiences);

        return new TokenValidationParameters
        {
            ValidateIssuer = _options.ValidateIssuer,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudiences = audiences,
            ValidateLifetime = _options.ValidateLifetime,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(_options.ClockSkewMinutes),
            ConfigurationManager = configManager as BaseConfigurationManager
        };
    }

    private AuthenticationResult BuildSuccessResult(TokenValidationResult result)
    {
        var principal = ExtractPrincipal(result);

        if (result.SecurityToken is JsonWebToken jwt && jwt.ValidTo != DateTime.MinValue)
            return AuthenticationResult.Success(principal, new DateTimeOffset(jwt.ValidTo));

        return AuthenticationResult.Success(principal, null);
    }

    private SecurityPrincipal ExtractPrincipal(TokenValidationResult result)
    {
        var claims = new List<SecurityClaim>();
        string? identifier = null;
        string? displayName = null;
        string? tenantId = null;

        foreach (var claim in result.Claims)
        {
            var value = claim.Value?.ToString() ?? string.Empty;

            if (claim.Key == _options.UserIdClaim) identifier = value;
            else if (claim.Key == _options.UserNameClaim) displayName = value;
            else if (claim.Key == _options.TenantIdClaim) tenantId = value;

            claims.Add(new SecurityClaim(claim.Key, value));
        }

        return new SecurityPrincipal(identifier, displayName, tenantId, claims);
    }

    private AuthenticationResult MapValidationFailure(TokenValidationResult result)
        => result.Exception is null
            ? AuthenticationResult.Invalid("Token validation failed")
            : MapException(result.Exception);

    private AuthenticationResult MapException(Exception ex)
    {
        return ex switch
        {
            SecurityTokenExpiredException =>
                LogAndReturn(LogLevel.Debug, ex, AuthenticationResult.Expired()),

            SecurityTokenInvalidSignatureException =>
                LogAndReturn(LogLevel.Warning, ex,
                    AuthenticationResult.Invalid("Invalid token signature")),

            SecurityTokenInvalidAudienceException =>
                LogAndReturn(LogLevel.Warning, ex,
                    AuthenticationResult.Invalid("Invalid token audience")),

            SecurityTokenInvalidIssuerException =>
                LogAndReturn(LogLevel.Warning, ex,
                    AuthenticationResult.Invalid("Invalid token issuer")),

            SecurityTokenException =>
                LogAndReturn(LogLevel.Warning, ex,
                    AuthenticationResult.Invalid("Token validation failed")),

            _ => LogAndReturn(LogLevel.Error, ex,
                    AuthenticationResult.Failure(
                        ValidationStatus.Invalid,
                        "Unexpected Azure AD authentication error"))
        };
    }

    private AuthenticationResult LogAndReturn(LogLevel level, Exception ex, AuthenticationResult result)
    {
        _logger.Log(level, ex, "Azure AD authentication failed: {Message}", ex.Message);
        return result;
    }
}
