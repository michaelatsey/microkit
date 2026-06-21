namespace MicroKit.Domain.Events;

/// <summary>
/// Marker interface for generic event concepts in the domain.
/// Dans ce modèle :
/// IEvent représente le concept générique d'événement ;
/// IDomainEvent représente un événement métier interne au domaine ;
/// IIntegrationEvent représente un événement destiné à être publié hors du bounded context ;
/// IApplicationEvent représente un événement technique ou applicatif.
/// </summary>
public interface IEvent;
