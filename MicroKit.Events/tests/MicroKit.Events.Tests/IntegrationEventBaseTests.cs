using FluentAssertions;
using MicroKit.Events;
using MicroKit.Events.Contracts;

namespace MicroKit.Events.Tests;

public class IntegrationEventBaseTests
{
    private sealed class OrderShipped : IntegrationEventBase
    {
        public string TrackingNumber { get; }

        public OrderShipped(string trackingNumber)
        {
            TrackingNumber = trackingNumber;
        }
    }

    private sealed class OrderShippedWithCorrelation : IntegrationEventBase
    {
        public OrderShippedWithCorrelation(string correlationId)
        {
            CorrelationId = correlationId;
        }
    }

    [Fact]
    public void Id_ShouldBeNonEmpty_OnConstruction()
    {
        var evt = new OrderShipped("TRACK-001");
        evt.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void TwoInstances_ShouldHaveDifferentIds()
    {
        var a = new OrderShipped("TRACK-001");
        var b = new OrderShipped("TRACK-002");
        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void MessageType_ShouldBeFullyQualifiedTypeName()
    {
        var evt = new OrderShipped("TRACK-001");
        evt.MessageType.Should().Be(typeof(OrderShipped).FullName);
    }

    [Fact]
    public void OccurredOnUtc_ShouldBeCloseToNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var evt = new OrderShipped("TRACK-001");
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        evt.OccurredOnUtc.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void CorrelationId_ShouldDefaultToNull()
    {
        var evt = new OrderShipped("TRACK-001");
        evt.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void ProtectedInit_ShouldAllowSettingCorrelationId()
    {
        var evt = new OrderShippedWithCorrelation("corr-42");
        evt.CorrelationId.Should().Be("corr-42");
    }

    [Fact]
    public void IntegrationEventBase_ShouldImplementIIntegrationEvent()
    {
        var evt = new OrderShipped("TRACK-001");
        evt.Should().BeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void IntegrationEventBase_ShouldImplementIEventBase()
    {
        var evt = new OrderShipped("TRACK-001");
        evt.Should().BeAssignableTo<IEventBase>();
    }

    [Fact]
    public void IEventBase_Id_ShouldMatchConcrete()
    {
        var evt = new OrderShipped("TRACK-001");
        IEventBase iBase = evt;
        iBase.Id.Should().Be(evt.Id);
    }

    [Fact]
    public void IEventBase_OccurredOnUtc_ShouldMatchConcrete()
    {
        var evt = new OrderShipped("TRACK-001");
        IEventBase iBase = evt;
        iBase.OccurredOnUtc.Should().Be(evt.OccurredOnUtc);
    }
}
