using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MicroKit.Auth.Jwt;

/// <summary>
/// Generates HMAC-SHA256 signed JWTs from an <see cref="ICurrentUser"/> context.
/// Intended for internal service-to-service use — see ADR-AUTH-007.
/// </summary>
/// <remarks>
/// <para>
/// This class does not manage token lifecycle (no refresh, no revocation, no storage).
/// Callers are responsible for transmitting and expiring the token appropriately.
/// </para>
/// <para>
/// Claims are derived from <see cref="IClaimsMapper.MapToClaims"/>. Register a custom
/// <see cref="IClaimsMapper"/> before calling <see cref="ServiceCollectionExtensions.AddMicroKitAuthJwt"/>
/// to control which claims appear in the generated token.
/// </para>
/// <para>Register via <see cref="ServiceCollectionExtensions.AddMicroKitAuthJwt"/>.</para>
/// <para>
/// This class is thread-safe and safe to register as a singleton: <see cref="JsonWebTokenHandler"/>
/// is stateless and all mutable state is confined to the <see cref="SecurityTokenDescriptor"/>
/// created per call.
/// </para>
/// </remarks>
public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly JwtOptions _options;
    private readonly IClaimsMapper _claimsMapper;
    private readonly SymmetricSecurityKey _key;

    /// <summary>
    /// Initialises the generator with the supplied <paramref name="options"/> and
    /// <paramref name="claimsMapper"/>.
    /// </summary>
    /// <param name="options">
    /// The validated JWT options. Must have a secret of at least 32 UTF-8 bytes.
    /// </param>
    /// <param name="claimsMapper">
    /// Converts <see cref="ICurrentUser"/> to the <see cref="System.Security.Claims.Claim"/>
    /// collection embedded in the token payload.
    /// </param>
    public JwtTokenGenerator(JwtOptions options, IClaimsMapper claimsMapper)
    {
        _options = options;
        _claimsMapper = claimsMapper;
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));
    }

    /// <inheritdoc />
    public ValueTask<Result<string>> GenerateAsync(ICurrentUser user, CancellationToken ct = default)
    {
        try
        {
            var claims = _claimsMapper.MapToClaims(user);
            var now = DateTime.UtcNow;
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = _options.Issuer,
                Audience = _options.Audience,
                Subject = new ClaimsIdentity(claims),
                NotBefore = now,
                Expires = now.Add(_options.Expiry),
                SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256)
            };
            var token = _handler.CreateToken(descriptor);
            return ValueTask.FromResult(Success(token));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ValueTask.FromResult(Failure<string>(new InvalidTokenError(null)));
        }
    }
}
