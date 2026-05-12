using MicroKit.Sample.OrderApi.Application.Abstractions;

namespace MicroKit.Sample.OrderApi.Domain.Events
{
    /// <summary>Domain event raised when a new order is created.</summary>
    /// <param name="OrderId">The identifier of the created order.</param>
    /// <param name="CustomerEmail">The customer email address.</param>
    public record OrderCreatedEvent(Guid OrderId, string CustomerEmail) : IDomainEvent
    {
        /// <inheritdoc/>
        public Guid Id => Guid.NewGuid();
        /// <inheritdoc/>
        public DateTimeOffset OccurredOnUtc => DateTimeOffset.UtcNow;
    }
}
