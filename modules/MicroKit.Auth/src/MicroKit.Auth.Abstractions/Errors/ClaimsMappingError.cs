namespace MicroKit.Auth.Errors;

/// <summary>
/// Raised when an <see cref="IClaimsMapper"/> implementation
/// cannot produce an <see cref="ICurrentUser"/> because a required
/// claim is absent from the <see cref="System.Security.Claims.ClaimsPrincipal"/>.
/// </summary>
/// <param name="MissingClaim">
/// The claim type that was expected but not found (e.g. <c>"sub"</c>, <c>"email"</c>).
/// </param>
public sealed record ClaimsMappingError(string MissingClaim)
    : AuthError(
        ErrorCode.From("AUTH.CLAIMS.MISSING"),
        $"Required claim '{MissingClaim}' was not present in the token.")
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.Unauthorized;
}
