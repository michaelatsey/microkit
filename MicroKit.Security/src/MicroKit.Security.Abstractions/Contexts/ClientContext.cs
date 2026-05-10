using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;
using System.Collections.ObjectModel;

namespace MicroKit.Security.Abstractions.Contexts;

/// <summary>
/// Implémentation immuable du contexte client utilisant un primary constructor.
/// Record scellé pour garantir l'immuabilité et optimiser la performance.
/// </summary>
/// <seealso cref="MicroKit.Security.Abstractions.Contexts.IClientContext" />
/// <seealso cref="System.IEquatable&lt;MicroKit.Security.Abstractions.Contexts.ClientContext&gt;" />
/// <param name="CorrelationId">Identifiant de corrélation unique.</param>
/// <param name="Principal">Principal de sécurité.</param>
/// <param name="Scheme">Schéma d'authentification utilisé.</param>
/// <param name="TenantId">Identifiant du tenant (optionnel).</param>
/// <param name="CreatedAt">Timestamp de création.</param>
/// <param name="Metadata"></param>
public sealed record ClientContext(
    string CorrelationId,
    ISecurityPrincipal Principal,
    AuthenticationScheme Scheme,
    string? TenantId,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, object> Metadata) : IClientContext
{
    /// <summary>
    /// Crée un contexte anonyme avec un nouvel identifiant de corrélation.
    /// </summary>
    /// <param name="timeProvider">Fournisseur de temps pour le timestamp.</param>
    /// <returns>Un nouveau contexte client anonyme.</returns>
    public static ClientContext Anonymous(TimeProvider timeProvider) => new(
        CorrelationId: Guid.NewGuid().ToString("N"),
        Principal: AnonymousPrincipal.Instance,
        Scheme: AuthenticationScheme.None,
        TenantId: null,
        CreatedAt: timeProvider.GetUtcNow(),
        Metadata: ReadOnlyDictionary<string, object>.Empty // Initialisation vide
    );

    /// <summary>
    /// Crée un contexte anonyme avec un identifiant de corrélation spécifié.
    /// </summary>
    /// <param name="correlationId">Identifiant de corrélation à utiliser.</param>
    /// <param name="timeProvider">Fournisseur de temps pour le timestamp.</param>
    /// <returns>Un nouveau contexte client anonyme.</returns>
    public static ClientContext Anonymous(string correlationId, TimeProvider timeProvider) => new(
        CorrelationId: correlationId,
        Principal: AnonymousPrincipal.Instance,
        Scheme: AuthenticationScheme.None,
        TenantId: null,
        CreatedAt: timeProvider.GetUtcNow(),
        Metadata: ReadOnlyDictionary<string, object>.Empty // Initialisation vide
    );

    /// <summary>
    /// Crée un nouveau contexte avec un tenant spécifié.
    /// </summary>
    /// <param name="tenantId">Identifiant du tenant.</param>
    /// <returns>Nouveau contexte avec le tenant mis à jour.</returns>
    public ClientContext WithTenant(string tenantId) => this with { TenantId = tenantId };

    /// <summary>
    /// Crée un nouveau contexte avec un principal différent.
    /// </summary>
    /// <param name="principal">Nouveau principal.</param>
    /// <param name="scheme">Schéma d'authentification.</param>
    /// <returns>Nouveau contexte authentifié.</returns>
    public ClientContext WithPrincipal(ISecurityPrincipal principal, AuthenticationScheme scheme) =>
        this with { Principal = principal, Scheme = scheme };

    /// <summary>
    /// Ajoute ou met à jour une métadonnée (utile pour le traçage du cache).
    /// </summary>
    public ClientContext WithMetadata(string key, object value)
    {
        var newMetadata = new Dictionary<string, object>(Metadata) { [key] = value };
        return this with { Metadata = newMetadata };
    }
}
