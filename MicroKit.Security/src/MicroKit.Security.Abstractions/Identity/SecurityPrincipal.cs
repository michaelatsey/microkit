namespace MicroKit.Security.Abstractions.Identity;

/// <summary>
/// Implémentation standard d'un principal de sécurité authentifié.
/// Record immuable optimisé pour la performance et la compatibilité AOT.
/// </summary>
/// <param name="Identifier">Identifiant unique du principal.</param>
/// <param name="DisplayName">Nom d'affichage du principal.</param>
/// <param name="TenantId">Identifiant unique du tenant du principal.</param>
/// <param name="Claims">Collection des claims associés.</param>
public sealed record SecurityPrincipal(
    string? Identifier,
    string? DisplayName,
    string? TenantId,
    IReadOnlyList<SecurityClaim> Claims) : ISecurityPrincipal
{
    /// <inheritdoc />
    public bool IsAuthenticated => !string.IsNullOrEmpty(Identifier);

    /// <inheritdoc />
    string? ISecurityPrincipal.Identifier => Identifier;

    /// <inheritdoc />
    string? ISecurityPrincipal.TenantId => TenantId; 

    /// <inheritdoc />
    public bool HasClaim(string type)
    {
        // On vérifie d'abord si le type correspond au TenantId pour plus de cohérence, 
        // bien qu'il soit déjà dans la propriété dédiée.
        foreach (var claim in Claims)
        {
            if (claim.IsType(type))
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    public bool HasClaim(string type, string value)
    {
        foreach (var claim in Claims)
        {
            if (claim.Matches(type, value))
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    public string? GetClaimValue(string type)
    {
        foreach (var claim in Claims)
        {
            if (claim.IsType(type))
                return claim.Value;
        }
        return null;
    }

    /// <summary>
    /// Crée un nouveau principal avec des claims additionnels.
    /// </summary>
    /// <param name="additionalClaims">Claims à ajouter.</param>
    /// <returns>Nouveau principal avec les claims combinés.</returns>
    public SecurityPrincipal WithClaims(params SecurityClaim[] additionalClaims)
    {
        if (additionalClaims.Length == 0) return this;

        var combinedClaims = new List<SecurityClaim>(Claims.Count + additionalClaims.Length);
        combinedClaims.AddRange(Claims);
        combinedClaims.AddRange(additionalClaims);

        return this with { Claims = combinedClaims };
    }

    /// <summary>
    /// Crée un nouveau principal associé à un tenant spécifique.
    /// Mais attention : dans un flux sécurisé, on ne devrait changer le TenantId de l'identité que si on est en train de "switcher" de contexte <br/>
    /// Exemple: un admin qui change de tenant
    /// </summary>
    public SecurityPrincipal WithTenant(string? identityTenantId)
    {
        return this with { TenantId = identityTenantId };
    }
}