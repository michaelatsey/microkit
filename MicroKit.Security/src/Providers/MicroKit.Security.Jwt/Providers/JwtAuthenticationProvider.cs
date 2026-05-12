namespace MicroKit.Security.Jwt.Providers;

using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Core.Providers;
using MicroKit.Security.Jwt.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;

/// <summary>
/// JWT authentication provider implementation.
/// </summary>
public sealed class JwtAuthenticationProvider : IAuthenticationProvider
{
    private readonly JwtOptions _options;
    private readonly JsonWebTokenHandler _tokenHandler;
    private readonly TokenValidationParameters _validationParameters;
    private readonly ILogger<JwtAuthenticationProvider> _logger;
    private readonly TimeProvider _timeProvider;
    // Gestionnaire de configuration pour les clés distantes (JWKS)
    private readonly IConfigurationManager<OpenIdConnectConfiguration>? _configManager;

    /// <summary>Initializes a new instance and builds token validation parameters from <paramref name="options"/>.</summary>
    /// <param name="options">JWT configuration.</param>
    /// <param name="timeProvider">Time provider for token lifetime calculations.</param>
    /// <param name="logger">Logger.</param>
    public JwtAuthenticationProvider(
        IOptions<JwtOptions> options,
        TimeProvider timeProvider,
        ILogger<JwtAuthenticationProvider> logger)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _tokenHandler = new JsonWebTokenHandler();

        // 1. Initialisation du ConfigManager si JwksUri est présent
        if (!string.IsNullOrEmpty(_options.Signing.JwksUri))
        {
            var retriever = new HttpDocumentRetriever { RequireHttps = _options.Validation.ValidateIssuer };

            _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                _options.Signing.JwksUri,
                new OpenIdConnectConfigurationRetriever(),
                retriever
            )
            {
                // On utilise ta propriété ici pour le rafraîchissement automatique
                AutomaticRefreshInterval = TimeSpan.FromMinutes(_options.Signing.JwksKeyRefreshMinutes),
                RefreshInterval = TimeSpan.FromMinutes(5) // Sécurité en cas d'échec
            };
        }
        _validationParameters = BuildValidationParameters();
    }

    /// <inheritdoc />
    public AuthenticationScheme Scheme => AuthenticationScheme.Jwt;

    /// <inheritdoc />
    public async ValueTask<AuthenticationResult> AuthenticateAsync(
        ReadOnlyMemory<char> credentials,
        CancellationToken cancellationToken = default)
    {
        if (credentials.IsEmpty)
        {
            return AuthenticationResult.Invalid("Token is required");
        }

        var token = credentials.ToString();

        try
        {
            // IMPORTANT : Si _configManager est présent, ValidateTokenAsync 
            // va automatiquement chercher les clés via le manager.
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

    private AuthenticationResult BuildSuccessResult(TokenValidationResult result)
    {
        var principal = ExtractPrincipal(result);

        if (result.SecurityToken is JsonWebToken jwt &&
            jwt.ValidTo != DateTime.MinValue)
        {
            return AuthenticationResult.Success(
                principal,
                new DateTimeOffset(jwt.ValidTo));
        }

        // Fallback au cas où ce ne serait pas un JsonWebToken (peu probable ici)
        return AuthenticationResult.Success(principal, null);
    }

    private TokenValidationParameters BuildValidationParameters()
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = _options.Validation.ValidateIssuer,
            ValidateAudience = _options.Validation.ValidateAudience,
            ValidateLifetime = _options.Validation.ValidateLifetime,
            ValidateIssuerSigningKey = _options.Validation.ValidateIssuerSigningKey,
            ClockSkew = TimeSpan.FromMinutes(_options.Validation.ClockSkewMinutes),

            // On attache le manager aux paramètres. 
            // Le TokenHandler l'utilisera pour résoudre les clés.
            ConfigurationManager = _configManager as BaseConfigurationManager
        };

        // Set valid issuers
        if (_options.Validation.ValidateIssuer)
        {
            var issuers = new List<string> { _options.Validation.Issuer };
            issuers.AddRange(_options.Validation.ValidIssuers);
            parameters.ValidIssuers = issuers;
        }

        // Configuration de l'Audience
        if (_options.Validation.ValidateAudience)
        {
            var audiences = new List<string> { _options.Validation.Audience };
            audiences.AddRange(_options.Validation.ValidAudiences);
            parameters.ValidAudiences = audiences;
        }

        // --- Logique de fallback pour les clés locales si pas de JWKS ---
        if (_configManager == null)
        {
            // Configuration de la Clé (Signing)
            if (!string.IsNullOrEmpty(_options.Signing.SecretKey))
            {
                parameters.IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(_options.Signing.SecretKey));
            }
            else if (!string.IsNullOrEmpty(_options.Signing.PublicKey))
            {
                var rsa = System.Security.Cryptography.RSA.Create();
                rsa.ImportFromPem(_options.Signing.PublicKey);
                parameters.IssuerSigningKey = new RsaSecurityKey(rsa);
            }
        }

        return parameters;
    }

    private SecurityPrincipal ExtractPrincipal(TokenValidationResult result)
    {
        var claims = new List<SecurityClaim>();
        string? identifier = null;
        string? displayName = null;
        string? tenantId = null;

        foreach (var claim in result.Claims)
        {
            var type = claim.Key;
            var value = claim.Value?.ToString() ?? string.Empty;

            // // Utilisation des Mappings définis dans les Options pour les claims standard
            if (type == _options.ClaimsMapping.UserIdClaim)
            {
                identifier = value;
            }
            else if (type == _options.ClaimsMapping.UserNameClaim)
            {
                displayName = value;
            }
            else if (type == _options.ClaimsMapping.TenantIdClaim)
            {
                tenantId = value;
            }

            // Gestion propre des rôles (si tableau JSON)
            if (type == _options.ClaimsMapping.RolesClaim && claim.Value is System.Text.Json.JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var role in jsonElement.EnumerateArray())
                    {
                        claims.Add(new SecurityClaim("role", role.GetString() ?? string.Empty));
                    }
                    continue;
                }
            }

            claims.Add(new SecurityClaim(type, value));
        }

        // --- AJOUT DE LA LOGIQUE RequireTenantClaim ---
        if (_options.Validation.RequireTenantClaim && string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("JWT validation failed: TenantId claim '{Claim}' is required but was not found in the token.",
                _options.ClaimsMapping.TenantIdClaim);

            // On lève une exception de sécurité que le bloc try/catch de AuthenticateAsync 
            // transformera en AuthenticationResult.Failure
            throw new SecurityTokenException($"Missing required tenant claim: {_options.ClaimsMapping.TenantIdClaim}");
        }

        return new SecurityPrincipal(
            Identifier: identifier,
            DisplayName: displayName,
            TenantId: tenantId,
            Claims: claims);
    }

    private AuthenticationResult MapValidationFailure(TokenValidationResult result)
    {
        return result.Exception is null
            ? AuthenticationResult.Invalid("Token validation failed")
            : MapException(result.Exception);
    }

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

            SecurityTokenNoExpirationException =>
                LogAndReturn(LogLevel.Warning, ex,
                    AuthenticationResult.Invalid("Token has no expiration")),

            SecurityTokenException =>
                LogAndReturn(LogLevel.Warning, ex,
                    AuthenticationResult.Invalid("Token validation failed")),

            _ =>
                LogAndReturn(LogLevel.Error, ex,
                    AuthenticationResult.Failure(
                        ValidationStatus.Invalid,
                        "Unexpected authentication error"))
        };
    }

    private AuthenticationResult LogAndReturn(
        LogLevel level,
        Exception ex,
        AuthenticationResult result)
    {
        _logger.Log(level, ex, "JWT authentication failed: {Message}", ex.Message);
        return result;
    }

}
