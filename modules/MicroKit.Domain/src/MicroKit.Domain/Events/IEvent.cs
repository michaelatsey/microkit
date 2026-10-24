namespace MicroKit.Domain.Events;

/// <summary>
/// Marker interface for every event concept in MicroKit.
/// </summary>
/// <remarks>
/// This is the canonical event root. Specialized event contracts derive from it:
/// <see cref="IDomainEvent"/> for facts raised by aggregates, and <c>IIntegrationEvent</c>
/// (MicroKit.Messaging) for messages crossing service boundaries.
/// </remarks>
public interface IEvent;
