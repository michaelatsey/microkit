using FluentAssertions;
using MicroKit.Events;
using MicroKit.Events.Contracts;

namespace MicroKit.Events.Tests;

public class EventBaseTests
{
    // Concrete implementation used only in tests
    private sealed class OrderCreated : EventBase
    {
        public string OrderId { get; }

        public OrderCreated(string orderId)
        {
            OrderId = orderId;
        }
    }

    private sealed class OrderCreatedWithMeta : EventBase
    {
        public OrderCreatedWithMeta(string correlationId, string idempotencyKey, string requestId, string causationId,
            IReadOnlyDictionary<string, string> metadata)
        {
            CorrelationId = correlationId;
            IdempotencyKey = idempotencyKey;
            RequestId = requestId;
            CausationId = causationId;
            Metadata = metadata;
        }
    }

    [Fact]
    public void Id_ShouldBeNonEmpty_OnConstruction()
    {
        var evt = new OrderCreated("order-1");
        evt.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void TwoInstances_ShouldHaveDifferentIds()
    {
        var a = new OrderCreated("order-1");
        var b = new OrderCreated("order-2");
        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void MessageType_ShouldBeFullyQualifiedTypeName()
    {
        var evt = new OrderCreated("order-1");
        evt.MessageType.Should().Be(typeof(OrderCreated).FullName);
    }

    [Fact]
    public void OccurredOnUtc_ShouldBeCloseToNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var evt = new OrderCreated("order-1");
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        evt.OccurredOnUtc.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void TimestampUtc_ShouldBeCloseToNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var evt = new OrderCreated("order-1");
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        evt.TimestampUtc.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void NullableFields_ShouldDefaultToNull()
    {
        var evt = new OrderCreated("order-1");

        evt.CorrelationId.Should().BeNull();
        evt.IdempotencyKey.Should().BeNull();
        evt.RequestId.Should().BeNull();
        evt.CausationId.Should().BeNull();
        evt.Metadata.Should().BeNull();
    }

    [Fact]
    public void ProtectedInit_ShouldAllowSettingFields()
    {
        var metadata = new Dictionary<string, string> { ["key"] = "value" };
        var evt = new OrderCreatedWithMeta(
            correlationId: "corr-1",
            idempotencyKey: "idem-1",
            requestId: "req-1",
            causationId: "cause-1",
            metadata: metadata);

        evt.CorrelationId.Should().Be("corr-1");
        evt.IdempotencyKey.Should().Be("idem-1");
        evt.RequestId.Should().Be("req-1");
        evt.CausationId.Should().Be("cause-1");
        evt.Metadata.Should().ContainKey("key").WhoseValue.Should().Be("value");
    }

    [Fact]
    public void EventBase_ShouldImplementIEvent()
    {
        var evt = new OrderCreated("order-1");
        evt.Should().BeAssignableTo<IEvent>();
    }

    [Fact]
    public void EventBase_ShouldImplementIEventBase()
    {
        var evt = new OrderCreated("order-1");
        evt.Should().BeAssignableTo<IEventBase>();
    }

    [Fact]
    public void Metadata_ShouldBeIReadOnlyDictionary()
    {
        var metadata = new Dictionary<string, string> { ["x"] = "y" };
        var evt = new OrderCreatedWithMeta("c", "i", "r", "ca", metadata);

        IEvent iEvent = evt;
        iEvent.Metadata.Should().BeAssignableTo<IReadOnlyDictionary<string, string>>();
    }
}
