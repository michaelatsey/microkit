namespace MicroKit.Security.Abstractions.Identity;

/// <summary>
/// Singleton anonymous principal for unauthenticated contexts.
/// Thread-safe singleton implementation.
/// </summary>
public sealed class AnonymousPrincipal : ISecurityPrincipal
{
    /// <summary>
    /// Singleton instance of the anonymous principal.
    /// </summary>
    public static readonly AnonymousPrincipal Instance = new();

    private AnonymousPrincipal() { }

    /// <inheritdoc />
    public string? Identifier => null;

    /// <inheritdoc />
    public string? DisplayName => "Anonymous";

    /// <inheritdoc />
    public string? TenantId => null;

    /// <inheritdoc />
    public bool IsAuthenticated => false;

    /// <inheritdoc />
    public IReadOnlyList<SecurityClaim> Claims => [];

    /// <inheritdoc />
    public bool HasClaim(string type) => false;

    /// <inheritdoc />
    public bool HasClaim(string type, string value) => false;

    /// <inheritdoc />
    public string? GetClaimValue(string type) => null;
}
