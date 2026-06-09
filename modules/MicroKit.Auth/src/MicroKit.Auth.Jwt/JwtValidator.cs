using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MicroKit.Auth.Jwt;

/// <summary>
/// HMAC-SHA256 JWT validator. Always returns <see cref="Result{T}"/> — never throws.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1 scope: HS256 only. RS256 and ES256 via JWKS are handled by provider packages
/// (<c>MicroKit.Auth.Supabase</c>, <c>MicroKit.Auth.OpenIdConnect</c>). See ADR-AUTH-007.
/// </para>
/// <para>Register via <see cref="ServiceCollectionExtensions.AddMicroKitAuthJwt"/>.</para>
/// <para>
/// This class is thread-safe and safe to register as a singleton: <see cref="JsonWebTokenHandler"/>
/// is stateless and <see cref="TokenValidationParameters"/> is not modified after construction.
/// </para>
/// <para>
/// <b>CancellationToken:</b> The <c>ct</c> parameter is accepted by the interface contract but
/// is not forwarded to <c>JsonWebTokenHandler.ValidateTokenAsync</c> in Phase 1 — the
/// Microsoft.IdentityModel 8.x overload used here does not accept a cancellation token.
/// HS256 validation is CPU-bound with no IO, so cancellation has no effect in practice.
/// When JWKS-based validation is added in Phase 2 provider packages, cancellation will be
/// wired through the HTTP client fetch.
/// </para>
/// </remarks>
public sealed class JwtValidator : IJwtValidator
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TokenValidationParameters _parameters;

    /// <summary>
    /// Initialises the validator from the supplied <paramref name="options"/>.
    /// </summary>
    /// <param name="options">
    /// The validated JWT options. Must have a secret of at least 32 UTF-8 bytes.
    /// </param>
    public JwtValidator(JwtOptions options)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));
        _parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ClockSkew = options.ClockSkew,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    }

    /// <inheritdoc />
    public async ValueTask<Result<ClaimsPrincipal>> ValidateAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Failure<ClaimsPrincipal>(new InvalidTokenError(null));

        try
        {
            var result = await _handler.ValidateTokenAsync(token, _parameters).ConfigureAwait(false);

            return result.IsValid
                ? Success(new ClaimsPrincipal(result.ClaimsIdentity))
                : Failure<ClaimsPrincipal>(new InvalidTokenError(SafeFragment(token)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure<ClaimsPrincipal>(new InvalidTokenError(SafeFragment(token)));
        }
    }

    private static string? SafeFragment(string token) =>
        token.Length >= 8 ? token[..8] : token.Length > 0 ? token : null;
}
