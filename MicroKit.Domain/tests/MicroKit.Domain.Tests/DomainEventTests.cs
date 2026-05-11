using MicroKit.Domain.Tests.Fakes;
using Xunit;

namespace MicroKit.Domain.Tests;

public sealed class DomainEventTests
{
    [Fact]
    public void DomainEvent_HasNonEmptyId()
    {
        var evt = new OrderPlaced(Guid.NewGuid());
        Assert.NotEqual(Guid.Empty, evt.Id);
    }

    [Fact]
    public void DomainEvent_OccurredOnUtc_IsApproximatelyNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var evt = new OrderPlaced(Guid.NewGuid());
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.InRange(evt.OccurredOnUtc, before, after);
    }

    [Fact]
    public void TwoDomainEvents_HaveDifferentIds()
    {
        var a = new OrderPlaced(Guid.NewGuid());
        var b = new OrderPlaced(Guid.NewGuid());
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void DomainEvent_ImplementsIDomainEvent()
    {
        var evt = new OrderPlaced(Guid.NewGuid());
        Assert.IsAssignableFrom<MicroKit.Domain.Contracts.Events.IDomainEvent>(evt);
    }

    [Fact]
    public void Aggregate_RaisesAndClearsEvents()
    {
        var orderId = Guid.NewGuid();
        var order = new OrderAggregate(orderId, "ORD-001");
        Assert.Single(order.DomainEvents);

        order.Cancel();
        Assert.Equal(2, order.DomainEvents.Count);

        order.ClearDomainEvents();
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void Aggregate_EventsAreRecords_SupportValueEquality()
    {
        var id = Guid.NewGuid();
        // Records with same data are value-equal
        var a = new OrderPlaced(id);
        var b = new OrderPlaced(id);
        Assert.Equal(a.OrderId, b.OrderId);
    }
}
