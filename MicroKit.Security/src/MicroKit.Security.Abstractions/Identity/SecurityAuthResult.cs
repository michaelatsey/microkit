using MicroKit.Security.Abstractions.Enums;

namespace MicroKit.Security.Abstractions.Identity;

/// <summary>
/// Represents the outcome of an authentication attempt.
/// </summary>
/// <param name="IsAuthenticated">Indicates whether at least one scheme authenticated successfully.</param>
/// <param name="Principal">The authenticated principal, or null on failure.</param>
/// <param name="Scheme">The specific scheme that validated the identity (e.g. Jwt, ApiKey).</param>
/// <param name="Metadata">Optional metadata attached to the authentication result.</param>
/// <param name="FailureMessage">Optional error message for logging purposes.</param>
public record SecurityAuthResult(
    bool IsAuthenticated,
    ISecurityPrincipal? Principal = null,
    AuthenticationScheme Scheme = AuthenticationScheme.None,
    IReadOnlyDictionary<string, object>? Metadata = null,
    string? FailureMessage = null
    )
{
    /// <summary>Creates a successful authentication result.</summary>
    public static SecurityAuthResult Success(ISecurityPrincipal principal, AuthenticationScheme scheme, IReadOnlyDictionary<string, object>? metadata = null)
        => new(true, principal, scheme, Metadata: metadata);

    /// <summary>Creates a failed authentication result.</summary>
    public static SecurityAuthResult Failure(string message = "Invalid credentials")
        => new(false, FailureMessage: message);
}
