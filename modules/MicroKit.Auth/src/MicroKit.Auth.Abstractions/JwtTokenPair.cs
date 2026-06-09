namespace MicroKit.Auth;

/// <summary>
/// A pair of access and refresh tokens produced by <see cref="IJwtRefreshTokenProvider"/>.
/// </summary>
/// <param name="AccessToken">The short-lived JWT access token.</param>
/// <param name="RefreshToken">The opaque one-time-use refresh token.</param>
public sealed record JwtTokenPair(string AccessToken, string RefreshToken);
