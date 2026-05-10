using MicroKit.Security.Abstractions.Enums;

namespace MicroKit.Security.Abstractions.Extraction;

/// <summary>
/// Résultat d'une tentative d'extraction depuis la requête HTTP.
/// </summary>
/// <param name="Value">La clé brute ou le token trouvé.</param>
/// <param name="Scheme">Le schéma détecté (ApiKey, Jwt, etc.).</param>
/// <param name="IsPrimaryCandidate"></param>
public record ExtractionResult(string? Value, AuthenticationScheme Scheme, bool IsPrimaryCandidate = true);