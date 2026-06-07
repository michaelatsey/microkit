namespace MicroKit.Auth.Errors;

/// <summary>
/// Raised when a JWT token fails validation for any reason — expired lifetime,
/// invalid signature, wrong audience, malformed structure, or missing required claims.
/// </summary>
/// <remarks>
/// <see cref="TokenFragment"/> contains only the first 8 characters of the raw token
/// for diagnostic tracing. This is safe to log. Never store or log the full token.
/// </remarks>
/// <param name="TokenFragment">
/// The first 8 characters of the raw token string, truncated to avoid leaking
/// sensitive material. <see langword="null"/> when the input token was null or
/// too short to produce a safe preview.
/// </param>
public sealed record InvalidTokenError(string? TokenFragment)
    : AuthError(
        ErrorCode.From("AUTH.TOKEN.INVALID"),
        "The provided JWT token is invalid or could not be validated.")
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.Unauthorized;
}
