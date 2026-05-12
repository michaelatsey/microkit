using MediatR;

namespace MicroKit.Sample.OrderApi.Application.Abstractions;

/// <summary>Marker interface for domain events published via MediatR.</summary>
public interface IDomainEvent : INotification
{
    /// <summary>Gets the unique identifier of this event instance.</summary>
    Guid Id { get; }
    /// <summary>Gets the UTC timestamp when the event occurred.</summary>
    DateTimeOffset OccurredOnUtc { get; }
}
