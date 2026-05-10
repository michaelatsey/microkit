namespace MicroKit.Security.Abstractions.Identity;

/// <summary>
/// Représente l'identité authentifiée d'un principal de sécurité.
/// Interface principale pour accéder aux informations d'identité dans l'écosystème MicroKit.
/// </summary>
public interface ISecurityPrincipal
{
    /// <summary>
    /// Identifiant unique du principal (ex: user ID, client ID).
    /// Peut être null pour les principaux anonymes.
    /// </summary>
    string? Identifier { get; }

    /// <summary>
    /// Nom d'affichage du principal.
    /// </summary>
    string? DisplayName { get; }

    /// <summary>
    /// Identifiant du tenant présent dans le JWT auquel appartient ce principal.
    /// Null si le principal n'est pas associé à un tenant spécifique
    /// ou si le multi-tenancy n'est pas utilisé.
    /// </summary>
    string? TenantId { get; } 

    /// <summary>
    /// Indique si le principal est authentifié.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Collection des claims associés au principal.
    /// </summary>
    IReadOnlyList<SecurityClaim> Claims { get; }

    /// <summary>
    /// Vérifie si le principal possède un claim du type spécifié.
    /// </summary>
    /// <param name="type">Le type du claim à rechercher.</param>
    /// <returns>True si un claim de ce type existe, false sinon.</returns>
    bool HasClaim(string type);

    /// <summary>
    /// Vérifie si le principal possède un claim avec le type et la valeur spécifiés.
    /// </summary>
    /// <param name="type">Le type du claim.</param>
    /// <param name="value">La valeur du claim.</param>
    /// <returns>True si le claim exact existe, false sinon.</returns>
    bool HasClaim(string type, string value);

    /// <summary>
    /// Récupère la valeur du premier claim correspondant au type spécifié.
    /// </summary>
    /// <param name="type">Le type du claim à rechercher.</param>
    /// <returns>La valeur du claim ou null si non trouvé.</returns>
    string? GetClaimValue(string type);
}

