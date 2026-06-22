namespace MicroKit.MediatR.Events;

/// <summary>
/// Compatibility shim for the former MediatR-local event root.
/// </summary>
/// <remarks>
/// New code must use <see cref="MicroKit.Domain.Events.IEvent"/> directly.
/// Domain-event dispatch contracts now require <see cref="MicroKit.Domain.Events.IDomainEvent"/>.
/// </remarks>
[Obsolete("Use MicroKit.Domain.Events.IEvent as the canonical MicroKit event root.")]
public interface IEvent : MicroKit.Domain.Events.IEvent;
