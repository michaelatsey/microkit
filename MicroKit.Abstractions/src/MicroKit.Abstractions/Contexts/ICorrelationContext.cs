namespace MicroKit.Abstractions.Contexts;

/// <summary>
/// Contrat pour le traçage distribué et la corrélation des logs.
/// Permet de suivre une requête technique à travers les différents composants du système.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>
    /// L'identifiant de corrélation unique pour la requête actuelle.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// Indique si le contexte a déjà été initialisé.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Récupère l'ID de corrélation actuel ou en génère un nouveau si nécessaire.
    /// </summary>
    /// <returns>L'identifiant de corrélation (string).</returns>
    string GetOrCreate();
}
