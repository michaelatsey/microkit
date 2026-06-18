namespace MicroKit.Messaging.UnitTests.Dispatch;

using MicroKit.Messaging.Dispatch;

public sealed class InProcessIntegrationDispatcherTests
{
    private readonly IMessageSerializer _serializer = Substitute.For<IMessageSerializer>();
    private readonly IMessagePublisher _publisher = Substitute.For<IMessagePublisher>();
    private readonly InProcessIntegrationDispatcher _sut;

    public InProcessIntegrationDispatcherTests()
        => _sut = new InProcessIntegrationDispatcher(_serializer, _publisher);

    [Fact]
    public async Task DispatchAsync_WhenPayloadValid_DeserializesAndPublishes()
    {
        var evt = new TestOrderPlacedEvent(new MessageId(Guid.NewGuid()), "t1");
        var message = MakeOutboxMessage(evt.GetType().AssemblyQualifiedName!, "{}");
        _serializer.Deserialize("{}",  message.EventType).Returns(evt);

        await _sut.DispatchAsync(message);

        await _publisher.Received(1).PublishAsync(evt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WhenDeserializeReturnsNull_ThrowsInvalidOperation()
    {
        var message = MakeOutboxMessage("SomeType", "{}");
        _serializer.Deserialize(Arg.Any<string>(), Arg.Any<string>()).Returns((IIntegrationEvent?)null);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await _sut.DispatchAsync(message));

        ex.Message.ShouldContain("SomeType");
    }

    [Fact]
    public async Task DispatchAsync_WhenPublisherThrows_PropagatesException()
    {
        var evt = new TestOrderPlacedEvent(new MessageId(Guid.NewGuid()), "t1");
        var message = MakeOutboxMessage(evt.GetType().AssemblyQualifiedName!, "{}");
        _serializer.Deserialize(Arg.Any<string>(), Arg.Any<string>()).Returns(evt);
        _publisher.PublishAsync(evt, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new Exception("transport error")));

        await Should.ThrowAsync<Exception>(async () => await _sut.DispatchAsync(message));
    }

    [Fact]
    public async Task DispatchAsync_WhenDeserializedEventIsConcreteType_SubscriberFoundByConcreteType()
    {
        // Ruling 6: even though T=IIntegrationEvent at the call site inside the dispatcher,
        // InProcessMessagePublisher uses evt.GetType() internally — so the concrete type
        // is preserved. This test verifies the dispatcher passes the deserialized event
        // through to the publisher correctly.
        var concreteEvt = new TestOrderPlacedEvent(new MessageId(Guid.NewGuid()), "t1");
        IIntegrationEvent deserializedAsBase = concreteEvt; // static type = IIntegrationEvent

        var message = MakeOutboxMessage(typeof(TestOrderPlacedEvent).AssemblyQualifiedName!, "{}");
        _serializer.Deserialize(Arg.Any<string>(), Arg.Any<string>()).Returns(deserializedAsBase);

        await _sut.DispatchAsync(message);

        // Publisher must receive the concrete object (not null, not a different instance)
        await _publisher.Received(1).PublishAsync(concreteEvt, Arg.Any<CancellationToken>());
    }

    private static OutboxMessage MakeOutboxMessage(string eventType, string payload) => new()
    {
        Id = new MessageId(Guid.NewGuid()),
        TenantId = "tenant-1",
        EventType = eventType,
        Payload = payload,
        Status = OutboxMessageStatus.Processing,
        RetryCount = 0,
        OccurredOnUtc = DateTimeOffset.UtcNow,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        CorrelationId = new CorrelationId(Guid.NewGuid())
    };
}

internal sealed record TestOrderPlacedEvent(
    MessageId MessageId,
    string TenantId) : IIntegrationEvent
{
    public CorrelationId? CorrelationId => null;
    public CausationId? CausationId => null;
    public DateTimeOffset OccurredOnUtc { get; } = DateTimeOffset.UtcNow;
}
