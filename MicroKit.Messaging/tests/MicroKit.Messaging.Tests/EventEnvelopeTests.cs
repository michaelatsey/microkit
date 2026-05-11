using FluentAssertions;
using MicroKit.Messaging.Abstractions.Common;

namespace MicroKit.Messaging.Tests;

public class EventEnvelopeTests
{
    private sealed record OrderPayload(string OrderId, decimal Amount);

    [Fact]
    public void EventEnvelope_GetPayload_ShouldReturnTypedPayload()
    {
        var payload = new OrderPayload("order-1", 99.99m);
        var envelope = new EventEnvelope<OrderPayload>
        {
            EventId = "evt-1",
            TenantId = "tenant-1",
            MessageType = "MyApp.OrderPayload",
            Payload = payload
        };

        var result = envelope.GetPayload();
        result.Should().BeOfType<OrderPayload>();
        ((OrderPayload)result).OrderId.Should().Be("order-1");
    }

    [Fact]
    public void EventEnvelope_BaseProperties_ShouldBeAccessibleViaBase()
    {
        var payload = new OrderPayload("order-2", 50m);
        var envelope = new EventEnvelope<OrderPayload>
        {
            EventId = "evt-2",
            TenantId = "tenant-2",
            MessageType = "MyApp.OrderPayload",
            Payload = payload,
            CorrelationId = "corr-1",
            CausationId = "cause-1",
            IdempotencyKey = "idem-1",
            OccurredOnUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PublishedAtUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 1, TimeSpan.Zero)
        };

        EventEnvelopeBase envBase = envelope;

        envBase.EventId.Should().Be("evt-2");
        envBase.TenantId.Should().Be("tenant-2");
        envBase.MessageType.Should().Be("MyApp.OrderPayload");
        envBase.CorrelationId.Should().Be("corr-1");
        envBase.CausationId.Should().Be("cause-1");
        envBase.IdempotencyKey.Should().Be("idem-1");
        envBase.OccurredOnUtc.Year.Should().Be(2024);
    }

    [Fact]
    public void EventEnvelope_GetPayload_ShouldReturnSameInstance()
    {
        var payload = new OrderPayload("order-3", 10m);
        var envelope = new EventEnvelope<OrderPayload>
        {
            EventId = "evt-3",
            TenantId = "t",
            MessageType = "T",
            Payload = payload
        };

        envelope.GetPayload().Should().BeSameAs(payload);
    }

    [Fact]
    public void EventEnvelope_Metadata_ShouldBeSettable()
    {
        var metadata = new Dictionary<string, string> { ["region"] = "eu-west-1" };
        var envelope = new EventEnvelope<string>
        {
            EventId = "e",
            TenantId = "t",
            MessageType = "T",
            Payload = "hello",
            Metadata = metadata
        };

        envelope.Metadata.Should().ContainKey("region").WhoseValue.Should().Be("eu-west-1");
    }

    [Fact]
    public void EventEnvelope_IsSubclassOf_EventEnvelopeBase()
    {
        typeof(EventEnvelope<string>).IsSubclassOf(typeof(EventEnvelopeBase)).Should().BeTrue();
    }

    [Fact]
    public void EventEnvelopeBase_GetPayload_IsAbstract()
    {
        var method = typeof(EventEnvelopeBase).GetMethod(nameof(EventEnvelopeBase.GetPayload));
        method.Should().NotBeNull();
        method!.IsAbstract.Should().BeTrue();
    }
}
