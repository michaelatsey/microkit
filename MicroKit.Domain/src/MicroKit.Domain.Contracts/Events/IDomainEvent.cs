
namespace MicroKit.Domain.Contracts.Events;

/// <summary>
/// Marqueur d'interface pour les événements de domaine
/// </summary>
public interface IDomainEvent
{
    /// <summary>Gets the unique identifier for this domain event.</summary>
    Guid Id { get; }
    /// <summary>
    /// Moment métier où l'événement s’est produit
    /// </summary>
    /// <value>
    /// The occurred on UTC.
    /// </value>
    DateTimeOffset OccurredOnUtc { get; }
}
