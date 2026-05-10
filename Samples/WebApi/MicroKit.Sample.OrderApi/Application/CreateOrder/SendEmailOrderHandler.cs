using MicroKit.Sample.OrderApi.Application.Abstractions;
using MicroKit.Sample.OrderApi.Domain.Events;

namespace MicroKit.Sample.OrderApi.Application.CreateOrder
{
    public class SendEmailOrderHandler : IDomainEventHandler<OrderCreatedEvent>
    {
        public async Task Handle(OrderCreatedEvent notification, CancellationToken ct)
        {
            // Logique d'envoi d'email
            Console.WriteLine($"[EMAIL] Confirmation envoyée à {notification.CustomerEmail}");
        }
    }
}
