namespace MicroKit.Messaging.UnitTests.Serialization;

using MicroKit.Messaging.Serialization;

public sealed class SystemTextJsonMessageSerializerTests
{
    private readonly SystemTextJsonMessageSerializer _sut = new();

    [Fact]
    public void Serialize_ThenDeserialize_ReturnsEquivalentEvent()
    {
        var evt = new TestOrderPlacedEvent(
            MessageId: new MessageId(Guid.NewGuid()),
            TenantId: "tenant-1",
            OrderId: Guid.NewGuid(),
            Amount: 99.99m);

        var payload = _sut.Serialize(evt);
        var deserialized = _sut.Deserialize(payload, typeof(TestOrderPlacedEvent).AssemblyQualifiedName!);

        deserialized.ShouldNotBeNull();
        deserialized.ShouldBeOfType<TestOrderPlacedEvent>();
        var typed = (TestOrderPlacedEvent)deserialized;
        typed.MessageId.ShouldBe(evt.MessageId);
        typed.TenantId.ShouldBe(evt.TenantId);
        typed.OrderId.ShouldBe(evt.OrderId);
        typed.Amount.ShouldBe(evt.Amount);
    }

    [Fact]
    public void Serialize_PreservesConcreteProperties_WhenStaticTypeIsInterface()
    {
        // Ruling 6: Serialize(IIntegrationEvent evt) uses evt.GetType(), not IIntegrationEvent.
        IIntegrationEvent evt = new TestOrderPlacedEvent(
            MessageId: new MessageId(Guid.NewGuid()),
            TenantId: "tenant-1",
            OrderId: Guid.NewGuid(),
            Amount: 42.00m);

        var payload = _sut.Serialize(evt);

        // Payload must contain concrete OrderId and Amount, not just interface members.
        payload.ShouldContain("orderId");
        payload.ShouldContain("amount");
    }

    [Fact]
    public void Deserialize_WhenEventTypeUnknown_ReturnsNull()
    {
        var result = _sut.Deserialize("{}", "NonExistent.Type.DoesNotExist, Nowhere");

        result.ShouldBeNull();
    }

    [Fact]
    public void Deserialize_WhenPayloadMalformed_ReturnsNull()
    {
        // Ruling 6-D: must return null, never throw.
        var result = _sut.Deserialize("{{not valid json{{", typeof(TestOrderPlacedEvent).AssemblyQualifiedName!);

        result.ShouldBeNull();
    }
}

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

internal sealed record TestOrderPlacedEvent(
    MessageId MessageId,
    string TenantId,
    Guid OrderId,
    decimal Amount) : IIntegrationEvent
{
    public CorrelationId? CorrelationId => null;
    public CausationId? CausationId => null;
    public DateTimeOffset OccurredOnUtc { get; } = DateTimeOffset.UtcNow;
}
