//namespace MicroKit.Abstractions.Contexts;

///// <summary>
///// Contrat de lecture pour le contexte de la clé API.
///// </summary>
//public interface IApiKeyContext
//{
//    /// <summary>
//    /// La clé API brute.
//    /// </summary>
//    string ApiKey { get; }

//    /// <summary>
//    /// L'identifiant technique associé à la clé (ex: ID du compte partenaire).
//    /// </summary>
//    string KeyIdentifier { get; }

//    /// <summary>
//    /// Indique si la clé a été extraite de la requête.
//    /// </summary>
//    bool IsResolved { get; }

//    /// <summary>
//    /// Indique si la clé a passé les tests de validité technique.
//    /// </summary>
//    bool IsValidated { get; }

//    /// <summary>
//    /// Garantit que le contexte est prêt à l'emploi.
//    /// </summary>
//    void EnsureResolved();
//}


//public interface IApiKeyValidator
//{
//    /// <summary>
//    /// Validates an API key.
//    /// </summary>
//    /// <param name="apiKey">The API key to validate.</param>
//    /// <param name="tenantId">The tenant identifier for context.</param>
//    /// <param name="clientId">The client identifier for context.</param>
//    /// <param name="cancellationToken">Cancellation token.</param>
//    /// <returns>Validation result with key identifier if valid.</returns>
//    Task<ApiKeyValidationResult> ValidateAsync(
//        string apiKey,
//        string tenantId,
//        string clientId,
//        CancellationToken cancellationToken = default);
//}

//public sealed record ApiKeyValidationResult
//{
//    public bool IsValid { get; init; }
//    public string KeyIdentifier { get; init; } = string.Empty;
//    public string[] Permissions { get; init; } = [];
//    public string? ErrorMessage { get; init; }

//    public static ApiKeyValidationResult Success(string keyIdentifier, string[] permissions) =>
//        new() { IsValid = true, KeyIdentifier = keyIdentifier, Permissions = permissions };

//    public static ApiKeyValidationResult Failure(string errorMessage) =>
//        new() { IsValid = false, ErrorMessage = errorMessage };
//}
