using System.Security.Claims;

namespace MicroKit.Auth;

/// <summary>
/// Provides bidirectional mapping between a <see cref="ClaimsPrincipal"/> and
/// <see cref="ICurrentUser"/>. Each identity provider (Supabase, Keycloak, etc.)
/// supplies its own implementation because claim names and structures differ.
/// </summary>
/// <remarks>
/// Implementations must be deterministic and side-effect-free.
/// The <see cref="MapFromClaims"/> direction is on the hot path (every request);
/// keep it allocation-efficient.
/// </remarks>
public interface IClaimsMapper
{
    /// <summary>
    /// Maps a validated <see cref="ClaimsPrincipal"/> to an <see cref="ICurrentUser"/>.
    /// </summary>
    /// <param name="principal">
    /// The <see cref="ClaimsPrincipal"/> produced by <see cref="IJwtValidator"/>.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <c>Result&lt;ICurrentUser&gt;.Success</c> with the mapped user on success;
    /// <c>Result&lt;ICurrentUser&gt;.Failure</c> with a
    /// <see cref="MicroKit.Auth.Errors.ClaimsMappingError"/> when a required claim
    /// (e.g. <c>sub</c>) is absent.
    /// </returns>
    Result<ICurrentUser> MapFromClaims(ClaimsPrincipal principal);

    /// <summary>
    /// Converts an <see cref="ICurrentUser"/> back into a sequence of <see cref="Claim"/> objects.
    /// Used by outbound contexts (e.g. service-to-service token enrichment).
    /// </summary>
    /// <param name="user">
    /// The user whose identity should be serialised to claims. Must not be <see langword="null"/>.
    /// </param>
    /// <returns>
    /// A non-null (possibly empty) sequence of <see cref="Claim"/> objects representing the user.
    /// </returns>
    IEnumerable<Claim> MapToClaims(ICurrentUser user);
}
