namespace MicroKit.Messaging.MediatR.UnitTests;

public sealed class MediatROutboxDispatcherTests
{
    private readonly IOutboxDispatcher _inner = Substitute.For<IOutboxDispatcher>();
    private readonly IMessageSerializer _serializer = Substitute.For<IMessageSerializer>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();

    private MediatROutboxDispatcher Build()
        => new(_inner, _serializer, _publisher, NullLogger<MediatROutboxDispatcher>.Instance);

    private static OutboxMessage MakeOutboxMessage(
        string eventType = "SomeType",
        string payload = "{}")
        => new()
        {
            Id = MessageId.New(),
            EventType = eventType,
            Payload = payload,
            TenantId = "tenant-1",
            Status = OutboxMessageStatus.Pending,
            RetryCount = 0,
            CorrelationId = CorrelationId.New(),
            CausationId = null,
            OccurredOnUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task DispatchAsync_WhenPayloadIsNotification_PublishesViaMediatR()
    {
        var notification = new FakeNotification();
        var message = MakeOutboxMessage();
        _serializer.Deserialize(message.Payload, message.EventType).Returns(notification);

        await Build().DispatchAsync(message);

        await _publisher.Received(1).Publish(notification, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WhenPayloadIsNotification_DoesNotDelegateToInner()
    {
        var message = MakeOutboxMessage();
        _serializer.Deserialize(message.Payload, message.EventType).Returns(new FakeNotification());

        await Build().DispatchAsync(message);

        await _inner.DidNotReceive().DispatchAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WhenPayloadIsIntegrationEvent_DelegatesToInner()
    {
        var message = MakeOutboxMessage();
        _serializer.Deserialize(message.Payload, message.EventType).Returns(new FakeIntegrationEvent());

        await Build().DispatchAsync(message);

        await _inner.Received(1).DispatchAsync(message, Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WhenDeserializeReturnsNull_DelegatesToInner()
    {
        var message = MakeOutboxMessage();
        _serializer.Deserialize(message.Payload, message.EventType).Returns((object?)null);

        await Build().DispatchAsync(message);

        await _inner.Received(1).DispatchAsync(message, Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class FakeNotification : INotification { }

    private sealed record FakeIntegrationEvent : IIntegrationEvent
    {
        public MessageId MessageId { get; } = MessageId.New();
        public string TenantId => "tenant-1";
        public CorrelationId? CorrelationId => null;
        public CausationId? CausationId => null;
        public DateTimeOffset OccurredOnUtc { get; } = DateTimeOffset.UtcNow;
    }
}
