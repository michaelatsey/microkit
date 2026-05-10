using MicroKit.Security.Abstractions.Extraction;
using Microsoft.AspNetCore.Http;

namespace MicroKit.Security.AspNetCore.Extraction;

/// <summary>
/// Contrat pour extraire des credentials depuis le transport HTTP.
/// </summary>
public interface IAuthenticationExtractor
{
    /// <summary>
    /// Priorité d'exécution (plus le chiffre est haut, plus il passe tôt).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Tente d'extraire les informations de la requête.
    /// </summary>
    ValueTask<ExtractionResult> ExtractCredentialsAsync(HttpContext context);
}
