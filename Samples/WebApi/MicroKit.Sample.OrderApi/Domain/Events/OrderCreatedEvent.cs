using MicroKit.Sample.OrderApi.Application.Abstractions;

namespace MicroKit.Sample.OrderApi.Domain.Events
{
    public record OrderCreatedEvent(Guid OrderId, string CustomerEmail) : IDomainEvent
    {
        public Guid Id => Guid.NewGuid();
        public DateTimeOffset OccurredOnUtc => DateTimeOffset.UtcNow;
    }
}
