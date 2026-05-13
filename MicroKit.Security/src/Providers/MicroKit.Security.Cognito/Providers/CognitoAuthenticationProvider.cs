using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Cognito.Options;
using MicroKit.Security.Core.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace MicroKit.Security.Cognito.Providers;

/// <summary>
/// Authentication provider that validates AWS Cognito tokens.
/// Keys are fetched from the User Pool's JWKS endpoint and cached with automatic rotation support.
/// </summary>
public sealed class CognitoAuthenticationProvider : IAuthenticationProvider
{
    private readonly CognitoOptions _options;
    private readonly JsonWebTokenHandler _tokenHandler;
    private readonly TokenValidationParameters _validationParameters;
    private readonly ILogger<CognitoAuthenticationProvider> _logger;

    /// <summary>Initializes a new instance and starts JWKS key discovery.</summary>
    /// <param name="options">Cognito configuration.</param>
    /// <param name="logger">Logger.</param>
    public CognitoAuthenticationProvider(
        IOptions<CognitoOptions> options,
        ILogger<CognitoAuthenticationProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _tokenHandler = new JsonWebTokenHandler();

        // Cognito exposes JWKS directly (not via OIDC discovery), so we use a JSON-only retriever
        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            _options.JwksUri,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true })
        {
            AutomaticRefreshInterval = TimeSpan.FromMinutes(_options.JwksKeyRefreshMinutes),
            RefreshInterval = TimeSpan.FromMinutes(5)
        };

        _validationParameters = BuildValidationParameters(configManager);
    }

    /// <inheritdoc />
    public AuthenticationScheme Scheme => AuthenticationScheme.Cognito;

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
        return new TokenValidationParameters
        {
            ValidateIssuer = _options.ValidateIssuer,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.ClientId,
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

        foreach (var claim in result.Claims)
        {
            var value = claim.Value?.ToString() ?? string.Empty;

            if (claim.Key == _options.UserIdClaim) identifier = value;
            else if (claim.Key == _options.UserNameClaim) displayName = value;

            // Expand Cognito group memberships into individual role claims
            if (claim.Key == _options.GroupsClaim &&
                claim.Value is System.Text.Json.JsonElement jsonElement &&
                jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var group in jsonElement.EnumerateArray())
                    claims.Add(new SecurityClaim("role", group.GetString() ?? string.Empty));

                continue;
            }

            claims.Add(new SecurityClaim(claim.Key, value));
        }

        return new SecurityPrincipal(identifier, displayName, null, claims);
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
                        "Unexpected Cognito authentication error"))
        };
    }

    private AuthenticationResult LogAndReturn(LogLevel level, Exception ex, AuthenticationResult result)
    {
        _logger.Log(level, ex, "Cognito authentication failed: {Message}", ex.Message);
        return result;
    }
}
