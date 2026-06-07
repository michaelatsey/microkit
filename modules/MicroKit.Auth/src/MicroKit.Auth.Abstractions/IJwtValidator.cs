using System.Security.Claims;

namespace MicroKit.Auth;

/// <summary>
/// Validates a raw JWT token string and, on success, returns the extracted
/// <see cref="ClaimsPrincipal"/>.
/// </summary>
/// <remarks>
/// <para>
/// This interface is deliberately non-throwing. Every failure — expired token,
/// invalid signature, wrong audience, missing claims — is returned as a
/// <c>Result&lt;ClaimsPrincipal&gt;.Failure</c> carrying a typed
/// <see cref="MicroKit.Auth.Errors.InvalidTokenError"/>. Never allow exceptions
/// to propagate to callers.
/// </para>
/// <para>
/// Implementations live in <c>MicroKit.Auth.Jwt</c> and are provider-specific
/// (e.g. <c>SupabaseJwtValidator</c>). This contract is intentionally thin to
/// allow multiple validation strategies without changing call sites.
/// </para>
/// <para>All async implementations must use <c>ConfigureAwait(false)</c>.</para>
/// </remarks>
public interface IJwtValidator
{
    /// <summary>
    /// Validates the JWT <paramref name="token"/> and extracts claims on success.
    /// </summary>
    /// <param name="token">The raw JWT string to validate. Must not be null or whitespace.</param>
    /// <param name="ct">
    /// A cancellation token (e.g. for JWKS network calls during key rotation).
    /// </param>
    /// <returns>
    /// <c>Result&lt;ClaimsPrincipal&gt;.Success</c> with the extracted principal on valid token;
    /// <c>Result&lt;ClaimsPrincipal&gt;.Failure</c> with an
    /// <see cref="MicroKit.Auth.Errors.InvalidTokenError"/> on any validation failure.
    /// Never throws.
    /// </returns>
    ValueTask<Result<ClaimsPrincipal>> ValidateAsync(string token, CancellationToken ct = default);
}
