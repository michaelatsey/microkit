namespace MicroKit.Security.Jwt.Services;

using MicroKit.Abstractions.Contexts;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Core.Utilities;
using MicroKit.Security.Jwt.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// JWT token generation and validation service.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly JsonWebTokenHandler _tokenHandler;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JwtTokenService> _logger;

    // Ajout du manager pour la validation
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configManager;

    /// <summary>Initializes a new instance and builds signing credentials from <paramref name="options"/>.</summary>
    /// <param name="options">JWT configuration.</param>
    /// <param name="timeProvider">Time provider for token generation timestamps.</param>
    /// <param name="logger">Logger.</param>
    public JwtTokenService(
        IOptions<JwtOptions> options,
        TimeProvider timeProvider,
        ILogger<JwtTokenService> logger)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _tokenHandler = new JsonWebTokenHandler();

        // 1. Initialisation du Manager (comme dans le Provider)
        if (!string.IsNullOrEmpty(_options.Signing.JwksUri))
        {
            _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                _options.Signing.JwksUri,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = _options.Validation.ValidateIssuer }
            )
            {
                AutomaticRefreshInterval = TimeSpan.FromMinutes(_options.Signing.JwksKeyRefreshMinutes),
                RefreshInterval = TimeSpan.FromMinutes(5)
            };
        }

        _signingCredentials = CreateSigningCredentials();
        _validationParameters = CreateValidationParameters();
    }

    /// <inheritdoc />
    public string GenerateAccessToken(
        ISecurityPrincipal principal,
        IEnumerable<SecurityClaim>? additionalClaims = null)
    {
        var now = _timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_options.Validation.AccessTokenExpirationMinutes);

        var claims = new Dictionary<string, object>
        {
            ["iss"] = _options.Validation.Issuer,
            ["aud"] = _options.Validation.Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expires.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        if (principal.Identifier is not null)
        {
            claims[_options.ClaimsMapping.UserIdClaim] = principal.Identifier;
        }

        if (principal.DisplayName is not null)
        {
            claims[_options.ClaimsMapping.UserNameClaim] = principal.DisplayName;
        }

        // Add principal claims
        var roles = new List<string>();
        foreach (var claim in principal.Claims)
        {
            if (claim.Type == "role" || claim.Type == "roles")
            {
                roles.Add(claim.Value);
            }
            else
            {
                claims[claim.Type] = claim.Value;
            }
        }

        if (roles.Count > 0)
        {
            claims[_options.ClaimsMapping.RolesClaim] = roles;
        }

        // Add additional claims
        if (additionalClaims is not null)
        {
            foreach (var claim in additionalClaims)
            {
                claims[claim.Type] = claim.Value;
            }
        }

        //if (principal is ITenantIdAccessor tenantAccessor && !string.IsNullOrEmpty(tenantAccessor.TenantId))
        if (!string.IsNullOrEmpty(principal.TenantId))
        {
            claims[_options.ClaimsMapping.TenantIdClaim] = principal.TenantId;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = claims,
            SigningCredentials = _signingCredentials
        };

        return _tokenHandler.CreateToken(descriptor);
    }

    /// <inheritdoc />
    public string GenerateRefreshToken()
    {
        return SecureTokenGenerator.GenerateRefreshToken();
    }

    /// <inheritdoc />
    public TokenPair GenerateTokenPair(
        ISecurityPrincipal principal,
        IEnumerable<SecurityClaim>? additionalClaims = null)
    {
        var now = _timeProvider.GetUtcNow();

        return new TokenPair(
            AccessToken: GenerateAccessToken(principal, additionalClaims),
            RefreshToken: GenerateRefreshToken(),
            AccessTokenExpires: now.AddMinutes(_options.Validation.AccessTokenExpirationMinutes),
            RefreshTokenExpires: now.AddDays(_options.Validation.RefreshTokenExpirationDays));
    }

    /// <inheritdoc />
    public async ValueTask<ISecurityPrincipal?> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _tokenHandler.ValidateTokenAsync(token, _validationParameters);

            if (!result.IsValid)
            {
                return null;
            }

            return ExtractPrincipal(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    /// <inheritdoc/>
    public TokenMetadata? GetTokenMetadata(string token)
    {
        try
        {
            var jwt = _tokenHandler.ReadJsonWebToken(token);
            return new TokenMetadata(
                ExpiresAt: jwt.ValidTo != DateTime.MinValue ? new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero) : DateTimeOffset.MinValue,
                Issuer: jwt.Issuer,
                Subject: jwt.Subject,
                TenantId: jwt.GetClaim(_options.ClaimsMapping.TenantIdClaim)?.Value
            );
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? GetTokenExpiration(string token)
    {
        try
        {
            var jwt = _tokenHandler.ReadJsonWebToken(token);
            return jwt.ValidTo != DateTime.MinValue
                ? new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private SigningCredentials CreateSigningCredentials()
    {
        if (!string.IsNullOrEmpty(_options.Signing.PrivateKey))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(_options.Signing.PrivateKey);
            return new SigningCredentials(
                new RsaSecurityKey(rsa),
                _options.Signing.Algorithm);
        }

        if (!string.IsNullOrEmpty(_options.Signing.SecretKey))
        {
            return new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Signing.SecretKey)),
                _options.Signing.Algorithm);
        }

        throw new InvalidOperationException("No signing key configured");
    }

    private TokenValidationParameters CreateValidationParameters()
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = _options.Validation.ValidateIssuer,
            ValidateAudience = _options.Validation.ValidateAudience,
            ValidateLifetime = _options.Validation.ValidateLifetime,
            ValidateIssuerSigningKey = _options.Validation.ValidateIssuerSigningKey,
            ValidIssuer = _options.Validation.Issuer,
            ValidAudience = _options.Validation.Audience,
            ClockSkew = TimeSpan.FromMinutes(_options.Validation.ClockSkewMinutes),

            // On lie le manager ici aussi !
            ConfigurationManager = _configManager as BaseConfigurationManager
        };

        // Fallback local si pas de JWKS (pour les tests ou clés symétriques)
        if (_configManager == null)
        {
            if (!string.IsNullOrEmpty(_options.Signing.SecretKey))
            {
                parameters.IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(_options.Signing.SecretKey));
            }
            else if (!string.IsNullOrEmpty(_options.Signing.PublicKey))
            {
                var rsa = RSA.Create();
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
        string? tenantId = null; // 1. Déclarer la variable

        foreach (var claim in result.Claims)
        {
            var value = claim.Value?.ToString() ?? string.Empty;

            if (claim.Key == _options.ClaimsMapping.UserIdClaim)
            {
                identifier = value;
            }
            else if (claim.Key == _options.ClaimsMapping.UserNameClaim)
            {
                displayName = value;
            }
            else if (claim.Key == _options.ClaimsMapping.TenantIdClaim) // 2. Extraire le claim tenant
            {
                tenantId = value;
            }

            claims.Add(new SecurityClaim(claim.Key, value));
        }

        // 3. Passer le tenantId au constructeur
        return new SecurityPrincipal(identifier, displayName, tenantId, claims);
    }
}
