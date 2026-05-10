namespace MicroKit.Security.Jwt.Services;

using MicroKit.Security.Abstractions.Identity;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;

/// <summary>
/// Service central pour la génération, la gestion et la validation des jetons JWT.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Génère un jeton d'accès (Access Token) pour un principal donné.
    /// L'implémentation doit inclure le TenantId du principal dans les claims.
    /// </summary>
    /// <param name="principal">Le principal de sécurité authentifié.</param>
    /// <param name="additionalClaims">Claims optionnels à ajouter au jeton.</param>
    /// <returns>Le JWT sous forme de chaîne de caractères.</returns>
    string GenerateAccessToken(
        ISecurityPrincipal principal,
        IEnumerable<SecurityClaim>? additionalClaims = null);

    /// <summary>
    /// Génère un jeton de rafraîchissement (Refresh Token) opaque ou auto-porté.
    /// </summary>
    /// <returns>Le jeton de rafraîchissement.</returns>
    string GenerateRefreshToken();

    /// <summary>
    /// Génère simultanément une paire de jetons (Access + Refresh).
    /// </summary>
    /// <param name="principal">Le principal de sécurité authentifié.</param>
    /// <param name="additionalClaims">Claims optionnels à ajouter.</param>
    /// <returns>Un enregistrement TokenPair contenant les jetons et leurs dates d'expiration.</returns>
    TokenPair GenerateTokenPair(
        ISecurityPrincipal principal,
        IEnumerable<SecurityClaim>? additionalClaims = null);

    /// <summary>
    /// Valide la signature et l'intégrité d'un jeton, puis extrait le principal.
    /// </summary>
    /// <param name="token">Le JWT à valider.</param>
    /// <param name="cancellationToken">Jeton d'annulation (utile pour les appels JWKS).</param>
    /// <returns>Le principal de sécurité si le jeton est valide, sinon null.</returns>
    ValueTask<ISecurityPrincipal?> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extrait les métadonnées d'un jeton (expiration, émetteur) sans nécessairement valider la signature.
    /// Utile pour les indices d'interface utilisateur ou les vérifications de pré-validation.
    /// </summary>
    /// <param name="token">Le JWT à analyser.</param>
    /// <returns>Les métadonnées du jeton ou null si le format est invalide.</returns>
    TokenMetadata? GetTokenMetadata(string token);
}

/// <summary>
/// Représente une paire de jetons d'authentification.
/// </summary>
public sealed record TokenPair(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpires,
    DateTimeOffset RefreshTokenExpires);

/// <summary>
/// Contient les informations descriptives extraites d'un jeton.
/// </summary>
public sealed record TokenMetadata(
    DateTimeOffset ExpiresAt,
    string Issuer,
    string? Subject,
    string? TenantId);