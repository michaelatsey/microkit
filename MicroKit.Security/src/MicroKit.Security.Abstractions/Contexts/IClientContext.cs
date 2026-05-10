using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;

namespace MicroKit.Security.Abstractions.Contexts;

/// <summary>
/// Contexte du client authentifié pour la requête courante.
/// Fournit un accès thread-safe aux informations d'identité.
/// </summary>
public interface IClientContext
{
    /// <summary>
    /// Identifiant unique de la requête/corrélation pour le traçage distribué.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// Principal de sécurité authentifié contenant l'identité et les claims.
    /// </summary>
    ISecurityPrincipal Principal { get; }

    /// <summary>
    /// Schéma d'authentification utilisé pour authentifier ce contexte.
    /// </summary>
    AuthenticationScheme Scheme { get; }

    /// <summary>
    /// Timestamp UTC de création du contexte.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Identifiant du contexte de location pour la requête courante.<br/>
    /// Hérite du TenantId du principal si disponible, mais peut être différent<br/>
    /// dans les scénarios d'impersonation ou de délégation.
    /// Null si le multi-tenancy n'est pas utilisé.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// Indique si le contexte représente un utilisateur authentifié.
    /// </summary>
    bool IsAuthenticated => Principal.IsAuthenticated;

    /// <summary>
    /// Métadonnées additionnelles extraites lors de l'authentification.
    /// Contient des informations sur le cache, la période de grâce ou des flags spécifiques au provider.
    /// </summary>
    IReadOnlyDictionary<string, object> Metadata { get; }
}
