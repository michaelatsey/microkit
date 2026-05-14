using MicroKit.Domain.Abstractions;
using OrderApi.Domain.Orders.ValueObjects;

namespace OrderApi.Domain.Orders.Events;

public sealed record OrderPlacedEvent(Guid OrderId, string TenantId, string CustomerId, Money TotalAmount)
    : DomainEvent;
