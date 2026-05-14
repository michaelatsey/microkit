using MicroKit.Domain.Abstractions;

namespace OrderApi.Domain.Orders.Events;

public sealed record OrderConfirmedEvent(Guid OrderId, string TenantId)
    : DomainEvent;
