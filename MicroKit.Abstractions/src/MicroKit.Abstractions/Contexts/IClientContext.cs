//namespace MicroKit.Abstractions.Contexts;

///// <summary>
///// Contrat de lecture pour le contexte Client (Restaurant).
///// Définit l'entité opérationnelle au sein d'un Tenant.
///// </summary>
//public interface IClientContext
//{
//    /// <summary>
//    /// Identifiant unique du client/restaurant (provenant du header X-Restaurant-Id).
//    /// </summary>
//    string ClientId { get; }

//    /// <summary>
//    /// Indique si l'identifiant client a été résolu pour la requête actuelle.
//    /// </summary>
//    bool IsResolved { get; }

//    /// <summary>
//    /// Garantit que le contexte client est résolu avant utilisation.
//    /// </summary>
//    /// <exception cref="InvalidOperationException">Si le contexte n'est pas résolu.</exception>
//    void EnsureResolved();
//}