namespace MicroKit.Domain.Events;

/// <summary>
/// Interface for entities that can provide and drain domain events.
/// Supports the collect-then-dispatch pattern where domain events are collected during
/// business operations and then dispatched by the Application/Infrastructure layers.
/// </summary>
/// <remarks>
/// <para>
/// The Domain layer is responsible for raising events that represent business facts.
/// The Application/Infrastructure layers are responsible for dispatching these events
/// to appropriate handlers, message buses, or external systems.
/// </para>
/// <para>
/// This separation ensures the Domain layer remains pure and framework-agnostic,
/// while allowing flexible event handling strategies in outer layers.
/// </para>
/// </remarks>
public interface IDomainEventsProvider : IHasDomainEvents
{
    /// <summary>
    /// Atomically retrieves all domain events and clears the internal collection.
    /// This method implements the "drain" pattern where events are collected and
    /// then removed in a single operation to prevent duplicate processing.
    /// </summary>
    /// <returns>
    /// A read-only collection containing all domain events that were raised.
    /// Returns an empty collection if no events were raised.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method should be called by the Application/Infrastructure layers
    /// after successfully persisting the aggregate's state changes.
    /// </para>
    /// <para>
    /// The returned collection is immutable to prevent external modification
    /// and ensure event integrity during the dispatch process.
    /// </para>
    /// </remarks>
    IReadOnlyCollection<IDomainEvent> DrainDomainEvents();
}