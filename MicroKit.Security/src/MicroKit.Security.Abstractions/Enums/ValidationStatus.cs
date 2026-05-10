namespace MicroKit.Security.Abstractions.Enums;

/// <summary>
/// Statuts possibles pour la validation d'authentification.
/// </summary>
public enum ValidationStatus : byte
{
    /// <summary>Statut inconnu ou non déterminé.</summary>
    Unknown = 0,

    /// <summary>Validation réussie.</summary>
    Valid = 1,

    /// <summary>Le token ou la clé a expiré.</summary>
    Expired = 2,

    /// <summary>Le token ou la clé a été révoqué.</summary>
    Revoked = 3,

    /// <summary>Le token ou la clé est invalide.</summary>
    Invalid = 4,

    /// <summary>Limite de taux atteinte.</summary>
    RateLimited = 5
}
