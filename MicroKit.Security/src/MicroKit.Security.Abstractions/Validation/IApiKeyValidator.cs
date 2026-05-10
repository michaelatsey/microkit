namespace MicroKit.Security.Abstractions.Validation;

/// <summary>
/// Contrat pour la validation des clés API avec support haute performance.
/// Utilise ValueTask et Span pour minimiser les allocations.
/// </summary>
public interface IApiKeyValidator
{
    /// <summary>
    /// Valide une clé API de manière asynchrone.
    /// </summary>
    /// <param name="apiKey">Clé API sous forme de string.</param>
    /// <param name="cancellationToken">Token d'annulation.</param>
    /// <returns>Résultat de validation avec le principal si valide.</returns>
    ValueTask<ApiKeyValidationResult> ValidateAsync(
        string apiKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Valide une clé API depuis un buffer de caractères (évite les allocations de string).
    /// </summary>
    /// <param name="apiKey">Clé API sous forme de span de caractères.</param>
    /// <param name="cancellationToken">Token d'annulation.</param>
    /// <returns>Résultat de validation avec le principal si valide.</returns>
    ValueTask<ApiKeyValidationResult> ValidateAsync(
        ReadOnlySpan<char> apiKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Valide une clé API depuis un buffer de bytes UTF-8 (performance maximale).
    /// </summary>
    /// <param name="apiKeyUtf8">Clé API encodée en UTF-8.</param>
    /// <param name="cancellationToken">Token d'annulation.</param>
    /// <returns>Résultat de validation avec le principal si valide.</returns>
    ValueTask<ApiKeyValidationResult> ValidateAsync(
        ReadOnlySpan<byte> apiKeyUtf8,
        CancellationToken cancellationToken = default);
}
