namespace MicroKit.Auth;

/// <summary>
/// Provides refresh token issuance and exchange.
/// </summary>
/// <remarks>
/// <para>
/// This contract is declared in Phase 1 to establish the public surface area.
/// The implementation (<c>JwtRefreshTokenProvider</c>), rotation strategy, and storage
/// abstraction (<c>IRefreshTokenStore</c>) are deferred to Phase 2.
/// </para>
/// <para>
/// Implementations must be one-time-use: a refresh token may only be exchanged once,
/// after which it is invalidated and a new pair is issued.
/// </para>
/// <para>All async implementations must use <c>ConfigureAwait(false)</c>.</para>
/// </remarks>
public interface IJwtRefreshTokenProvider
{
    /// <summary>
    /// Issues a new opaque refresh token for the given <paramref name="user"/>.
    /// </summary>
    /// <param name="user">
    /// The authenticated user for whom the refresh token is issued.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// <c>Result&lt;string&gt;.Success</c> with the opaque refresh token on success;
    /// <c>Result&lt;string&gt;.Failure</c> if the token could not be issued.
    /// Never throws.
    /// </returns>
    ValueTask<Result<string>> IssueAsync(ICurrentUser user, CancellationToken ct = default);

    /// <summary>
    /// Exchanges a valid refresh token for a new access/refresh token pair.
    /// The supplied <paramref name="refreshToken"/> is invalidated after a successful exchange.
    /// </summary>
    /// <param name="refreshToken">
    /// The opaque refresh token previously issued by <see cref="IssueAsync"/>.
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// <c>Result&lt;JwtTokenPair&gt;.Success</c> with the new token pair on success;
    /// <c>Result.Failure</c> when the refresh token is invalid, expired, or has already
    /// been used. Never throws.
    /// </returns>
    ValueTask<Result<JwtTokenPair>> ExchangeAsync(
        string refreshToken,
        CancellationToken ct = default);
}
