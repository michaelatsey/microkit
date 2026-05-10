using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;

namespace MicroKit.Security.Abstractions.Validation;

/// <summary>
/// Résultat immuable de la validation d'une clé API.
/// Utilise required init pour garantir l'initialisation correcte.
/// </summary>
public sealed record ApiKeyValidationResult
{
    /// <summary>
    /// Statut de la validation.
    /// </summary>
    public required ValidationStatus Status { get; init; }

    /// <summary>
    /// Principal de sécurité associé à la clé (null si validation échouée).
    /// </summary>
    public required ISecurityPrincipal? Principal { get; init; }

    /// <summary>
    /// Message d'erreur en cas d'échec de validation.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Métadonnées additionnelles associées à la validation.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Indique si la validation a réussi.
    /// </summary>
    public bool IsValid => Status == ValidationStatus.Valid && Principal is not null;

    /// <summary>
    /// Crée un résultat de validation réussie.
    /// </summary>
    /// <param name="principal">Le principal authentifié.</param>
    /// <returns>Résultat de succès.</returns>
    public static ApiKeyValidationResult Success(ISecurityPrincipal principal) => new()
    {
        Status = ValidationStatus.Valid,
        Principal = principal
    };

    /// <summary>
    /// Crée un résultat de validation réussie avec métadonnées.
    /// </summary>
    /// <param name="principal">Le principal authentifié.</param>
    /// <param name="metadata">Métadonnées additionnelles.</param>
    /// <returns>Résultat de succès avec métadonnées.</returns>
    public static ApiKeyValidationResult Success(
        ISecurityPrincipal principal,
        IReadOnlyDictionary<string, object> metadata) => new()
        {
            Status = ValidationStatus.Valid,
            Principal = principal,
            Metadata = metadata
        };

    /// <summary>
    /// Crée un résultat d'échec de validation.
    /// </summary>
    /// <param name="status">Le statut d'échec.</param>
    /// <param name="message">Message d'erreur optionnel.</param>
    /// <returns>Résultat d'échec.</returns>
    public static ApiKeyValidationResult Failure(ValidationStatus status, string? message = null) => new()
    {
        Status = status,
        Principal = null,
        ErrorMessage = message
    };

    /// <summary>
    /// Crée un résultat pour une clé invalide.
    /// </summary>
    /// <param name="message">Message d'erreur optionnel.</param>
    /// <returns>Résultat d'invalidité.</returns>
    public static ApiKeyValidationResult Invalid(string? message = null) =>
        Failure(ValidationStatus.Invalid, message ?? "The API key is invalid.");

    /// <summary>
    /// Crée un résultat pour une clé expirée.
    /// </summary>
    /// <param name="message">Message d'erreur optionnel.</param>
    /// <returns>Résultat d'expiration.</returns>
    public static ApiKeyValidationResult Expired(string? message = null) =>
        Failure(ValidationStatus.Expired, message ?? "The API key has expired.");

    /// <summary>
    /// Crée un résultat pour une clé révoquée.
    /// </summary>
    /// <param name="message">Message d'erreur optionnel.</param>
    /// <returns>Résultat de révocation.</returns>
    public static ApiKeyValidationResult Revoked(string? message = null) =>
        Failure(ValidationStatus.Revoked, message ?? "The API key has been revoked.");

    /// <summary>
    /// Crée un résultat pour une limite de taux atteinte.
    /// </summary>
    /// <param name="message">Message d'erreur optionnel.</param>
    /// <returns>Résultat de limitation.</returns>
    public static ApiKeyValidationResult RateLimited(string? message = null) =>
        Failure(ValidationStatus.RateLimited, message ?? "Rate limit exceeded.");
}
