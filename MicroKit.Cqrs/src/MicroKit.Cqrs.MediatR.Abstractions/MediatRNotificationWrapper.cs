using MediatR;
using MicroKit.Domain.Abstractions;

namespace MicroKit.Cqrs.MediatR.Abstractions;

/// <summary>MediatR <see cref="INotification"/> wrapper that adapts a <typeparamref name="TEvent"/> domain event for MediatR dispatch.</summary>
/// <typeparam name="TEvent">The domain event type being wrapped.</typeparam>
public class MediatRNotificationWrapper<TEvent> : INotification
    where TEvent : DomainEvent
{
    /// <summary>Gets the wrapped domain event.</summary>
    public TEvent DomainEvent { get; }
    /// <summary>Initializes a new instance wrapping the given <paramref name="domainEvent"/>.</summary>
    /// <param name="domainEvent">The domain event to wrap.</param>
    public MediatRNotificationWrapper(TEvent domainEvent) => DomainEvent = domainEvent;
}
