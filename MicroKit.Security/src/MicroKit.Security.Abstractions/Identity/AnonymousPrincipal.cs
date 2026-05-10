namespace MicroKit.Security.Abstractions.Identity;

/// <summary>
/// Principal anonyme singleton pour les contextes non authentifiés.
/// Implémentation thread-safe utilisant le pattern singleton.
/// </summary>
public sealed class AnonymousPrincipal : ISecurityPrincipal
{
    /// <summary>
    /// Instance singleton du principal anonyme.
    /// </summary>
    public static readonly AnonymousPrincipal Instance = new();

    /// <summary>
    /// Constructeur privé pour garantir le pattern singleton.
    /// </summary>
    private AnonymousPrincipal() { }

    /// <inheritdoc />
    public string? Identifier => null;

    /// <inheritdoc />
    public string? DisplayName => "Anonymous";

    /// <summary>
    /// Un utilisateur anonyme n'a pas de TenantId par défaut.
    /// </summary>
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
