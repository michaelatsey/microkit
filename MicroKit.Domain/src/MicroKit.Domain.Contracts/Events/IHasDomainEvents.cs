namespace MicroKit.Domain.Contracts.Events;

/// <summary>
/// 
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>
    /// Gets the domain events.
    /// </summary>
    /// <value>
    /// The domain events.
    /// </value>
    IReadOnlyCollection<IDomainEvent>? DomainEvents { get; }

    /// <summary>
    /// Clears the domain events.
    /// </summary>
    void ClearDomainEvents();
}
