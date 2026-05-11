using MicroKit.Domain.Tests.Fakes;
using Xunit;

namespace MicroKit.Domain.Tests;

public sealed class AggregateRootTests
{
    [Fact]
    public void Constructor_RaisesDomainEvent()
    {
        var id = Guid.NewGuid();
        var order = new OrderAggregate(id, "ORD-001");
        var evt = Assert.Single(order.DomainEvents);
        Assert.IsType<OrderPlaced>(evt);
        Assert.Equal(id, ((OrderPlaced)evt).OrderId);
    }

    [Fact]
    public void IncrementVersion_StartsAtZeroAndIncrements()
    {
        var order = new OrderAggregate(Guid.NewGuid(), "ORD-001");
        Assert.Equal(1, order.Version);
        order.Cancel();
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void ClearDomainEvents_EmptiesCollection()
    {
        var order = new OrderAggregate(Guid.NewGuid(), "ORD-001");
        Assert.NotEmpty(order.DomainEvents);
        order.ClearDomainEvents();
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void RemoveDomainEvent_RemovesSpecificEvent()
    {
        var order = new OrderAggregate(Guid.NewGuid(), "ORD-001");
        var placed = order.DomainEvents.First();
        order.RemoveEvent(placed);
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void Cancel_AddsSecondDomainEvent()
    {
        var order = new OrderAggregate(Guid.NewGuid(), "ORD-001");
        order.Cancel();
        Assert.Equal(2, order.DomainEvents.Count);
        Assert.IsType<OrderCancelled>(order.DomainEvents.Last());
    }

    [Fact]
    public void DomainEvents_IsReadOnly()
    {
        var order = new OrderAggregate(Guid.NewGuid(), "ORD-001");
        Assert.IsAssignableFrom<IReadOnlyCollection<MicroKit.Domain.Contracts.Events.IDomainEvent>>(order.DomainEvents);
    }

    [Fact]
    public void ImplementsIAggregateRoot_WhichExtendsIHasDomainEvents()
    {
        var order = new OrderAggregate(Guid.NewGuid(), "ORD-001");
        Assert.IsAssignableFrom<MicroKit.Domain.Contracts.IAggregateRoot>(order);
        Assert.IsAssignableFrom<MicroKit.Domain.Contracts.Events.IHasDomainEvents>(order);
    }
}
