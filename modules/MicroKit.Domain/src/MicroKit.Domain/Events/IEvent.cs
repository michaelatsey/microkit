namespace MicroKit.Domain.Events;

/// <summary>
/// Marker interface for every event concept in MicroKit.
/// </summary>
/// <remarks>
/// This is the canonical event root. Specialized event contracts derive from it:
/// <c>IDomainEvent</c> for facts raised by aggregates, <c>IIntegrationEvent</c>
/// for messages crossing service boundaries, and <c>IApplicationEvent</c> for
/// application-level facts.
/// </remarks>
public interface IEvent;
