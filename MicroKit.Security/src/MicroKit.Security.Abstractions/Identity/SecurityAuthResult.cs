using MicroKit.Security.Abstractions.Enums;

namespace MicroKit.Security.Abstractions.Identity;

/// <summary>
/// Représente le résultat d'une tentative d'authentification globale.
/// </summary>
/// <param name="IsAuthenticated">Indique si l'authentification a réussi pour au moins un schéma.</param>
/// <param name="Principal">Le principal authentifié (ou null en cas d'échec).</param>
/// <param name="Scheme">Le schéma spécifique qui a validé l'identité (ex: Jwt, ApiKey).</param>
/// <param name="Metadata"></param>
/// <param name="FailureMessage">Message d'erreur optionnel pour le logging.</param>
public record SecurityAuthResult(
    bool IsAuthenticated,
    ISecurityPrincipal? Principal = null,
    AuthenticationScheme Scheme = AuthenticationScheme.None,
    IReadOnlyDictionary<string, object>? Metadata = null,
    string? FailureMessage = null
    )
{
    // Helpers statiques pour faciliter l'usage dans le SecurityService
    public static SecurityAuthResult Success(ISecurityPrincipal principal, AuthenticationScheme scheme, IReadOnlyDictionary<string, object>? metadata = null)
        => new(true, principal, scheme, Metadata: metadata);

    public static SecurityAuthResult Failure(string message = "Invalid credentials")
        => new(false, FailureMessage: message);
}