using MediatR;

namespace MicroKit.Sample.OrderApi.Application.Abstractions;

public interface IDomainEvent : INotification
{
    Guid Id { get; }
    DateTimeOffset OccurredOnUtc { get; }
}
