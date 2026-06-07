using System.Security.Claims;

namespace MicroKit.Auth;

/// <summary>
/// Extracts a flat dictionary of claims from a <see cref="ClaimsPrincipal"/>.
/// Used by <see cref="IClaimsMapper"/> implementations to access claims by type
/// without dealing with the <see cref="ClaimsPrincipal"/> API directly.
/// </summary>
/// <remarks>
/// When a claim type appears multiple times in the principal, implementations should
/// document their de-duplication strategy. The convention across MicroKit.Auth is
/// last-wins for scalar claims.
/// </remarks>
public interface IJwtClaimsReader
{
    /// <summary>
    /// Reads all claims from the <paramref name="principal"/> into a flat string dictionary.
    /// </summary>
    /// <param name="principal">
    /// The <see cref="ClaimsPrincipal"/> to extract claims from.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <returns>
    /// A read-only dictionary mapping claim type to claim value.
    /// Empty when no claims are present; never <see langword="null"/>.
    /// </returns>
    IReadOnlyDictionary<string, string> ReadClaims(ClaimsPrincipal principal);
}
