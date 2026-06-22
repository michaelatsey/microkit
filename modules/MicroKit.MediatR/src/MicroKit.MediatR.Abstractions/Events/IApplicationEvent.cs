namespace MicroKit.MediatR.Events;

/// <summary>
/// Represents an application-level event: a fact emitted by the application layer
/// rather than by an aggregate or an external integration boundary.
/// </summary>
public interface IApplicationEvent : MicroKit.Domain.Events.IEvent;
