namespace MicroKit.Security.Jwt.Services;

using MicroKit.Security.Abstractions.Identity;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;

/// <summary>Central service for generating, managing, and validating JWT tokens.</summary>
public interface IJwtTokenService
{
    /// <summary>Generates an access token for the given principal.</summary>
    /// <param name="principal">The authenticated security principal.</param>
    /// <param name="additionalClaims">Optional additional claims to embed in the token.</param>
    /// <returns>The signed JWT as a compact string.</returns>
    string GenerateAccessToken(
        ISecurityPrincipal principal,
        IEnumerable<SecurityClaim>? additionalClaims = null);

    /// <summary>Generates an opaque refresh token.</summary>
    /// <returns>A cryptographically random refresh token string.</returns>
    string GenerateRefreshToken();

    /// <summary>Generates an access/refresh token pair in a single call.</summary>
    /// <param name="principal">The authenticated security principal.</param>
    /// <param name="additionalClaims">Optional additional claims to embed in the access token.</param>
    /// <returns>A <see cref="TokenPair"/> containing both tokens and their expiry times.</returns>
    TokenPair GenerateTokenPair(
        ISecurityPrincipal principal,
        IEnumerable<SecurityClaim>? additionalClaims = null);

    /// <summary>Validates the signature and integrity of a token, then extracts the principal.</summary>
    /// <param name="token">The JWT to validate.</param>
    /// <param name="cancellationToken">Cancellation token (used for JWKS key fetches).</param>
    /// <returns>The security principal if the token is valid; otherwise <c>null</c>.</returns>
    ValueTask<ISecurityPrincipal?> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads token metadata (expiry, issuer) without validating the signature.
    /// Useful for UI hints or pre-validation checks.
    /// </summary>
    /// <param name="token">The JWT to read.</param>
    /// <returns>The token metadata, or <c>null</c> if the format is invalid.</returns>
    TokenMetadata? GetTokenMetadata(string token);
}

/// <summary>Represents an access/refresh token pair.</summary>
public sealed record TokenPair(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpires,
    DateTimeOffset RefreshTokenExpires);

/// <summary>Descriptive metadata extracted from a JWT without full validation.</summary>
public sealed record TokenMetadata(
    DateTimeOffset ExpiresAt,
    string Issuer,
    string? Subject,
    string? TenantId);
