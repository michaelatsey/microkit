namespace MicroKit.Auth;

/// <summary>
/// Generates a signed JWT from an authenticated user's identity context.
/// </summary>
/// <remarks>
/// <para>
/// This is a scoped exception to the general "validation only" rule for Auth packages.
/// Only <c>MicroKit.Auth.Jwt</c> implements this interface — see ADR-AUTH-007 for the
/// full rationale and constraints.
/// </para>
/// <para>
/// The generated token is signed with the HMAC key configured in <c>JwtOptions.Secret</c>.
/// No lifecycle management is performed (no refresh, no revocation, no storage).
/// Callers are responsible for transmitting and expiring the token appropriately.
/// </para>
/// <para>All async implementations must use <c>ConfigureAwait(false)</c>.</para>
/// </remarks>
public interface IJwtTokenGenerator
{
    /// <summary>
    /// Generates a signed JWT whose claims are derived from the given <paramref name="user"/>.
    /// </summary>
    /// <param name="user">
    /// The authenticated user whose identity is encoded into the token payload.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// <c>Result&lt;string&gt;.Success</c> with the compact-serialised JWT on success;
    /// <c>Result&lt;string&gt;.Failure</c> with a typed error if the token could not be
    /// generated. Never throws.
    /// </returns>
    ValueTask<Result<string>> GenerateAsync(ICurrentUser user, CancellationToken ct = default);
}
