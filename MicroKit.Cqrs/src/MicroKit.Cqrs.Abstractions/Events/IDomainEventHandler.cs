using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Cqrs.Abstractions.Events;

/// <summary>
/// Interface marqueur pour identifier tous les gestionnaires d'événements 
/// sans connaître le type générique à l'avance (utile pour la réflexion/DI).
/// </summary>
public interface IDomainEventHandler { }

/// <summary>
/// Interface pour les gestionnaires d'événements de domaine
/// </summary>
/// <typeparam name="TEvent">Le type d'événement à traiter</typeparam>
public interface IDomainEventHandler<in TEvent> : IDomainEventHandler
    where TEvent : IDomainEvent
{
    /// <summary>
    /// Handles the asynchronous.
    /// </summary>
    /// <param name="event">The event.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    Task Handle(TEvent @event, CancellationToken cancellationToken = default);
}
