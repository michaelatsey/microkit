namespace MicroKit.Messaging.UnitTests.Publishing;

using Microsoft.Extensions.Logging;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Registry;

public sealed class InProcessMessagePublisherTests
{
    private readonly MessageHandlerRegistry _registry = new();
    private readonly IInboxStore _inboxStore = Substitute.For<IInboxStore>();
    private readonly IMessageSerializer _serializer = Substitute.For<IMessageSerializer>();
    private readonly ILogger<InProcessMessagePublisher> _logger = Substitute.For<ILogger<InProcessMessagePublisher>>();
    private readonly InProcessMessagePublisher _sut;

    public InProcessMessagePublisherTests()
    {
        _serializer.Serialize(Arg.Any<IIntegrationEvent>()).Returns("{}");
        _sut = new InProcessMessagePublisher(_registry, _inboxStore, _serializer, _logger);
    }

    [Fact]
    public async Task PublishAsync_WhenSubscriberRegistered_WritesInboxRow()
    {
        _registry.Register(typeof(OrderPlacedEvent), "TestConsumer", typeof(object));
        var evt = MakeEvent();

        await _sut.PublishAsync(evt);

        await _inboxStore.Received(1).AddAsync(
            Arg.Is<InboxMessage>(m => m.ConsumerType == "TestConsumer" && m.TenantId == evt.TenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenMultipleSubscribersRegistered_WritesRowPerConsumer()
    {
        _registry.Register(typeof(OrderPlacedEvent), "ConsumerA", typeof(object));
        _registry.Register(typeof(OrderPlacedEvent), "ConsumerB", typeof(object));
        var evt = MakeEvent();

        await _sut.PublishAsync(evt);

        await _inboxStore.Received(2).AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenNoSubscriberRegistered_LogsWarningAndReturns()
    {
        var evt = MakeEvent();

        await _sut.PublishAsync(evt);

        await _inboxStore.DidNotReceive().AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_UsesRuntimeTypeNotGenericTypeForLookup()
    {
        // Ruling 6: registry lookup must use evt.GetType(), not typeof(T).
        // When called with T=IIntegrationEvent (as InProcessIntegrationDispatcher does),
        // the concrete type must still resolve subscribers.
        _registry.Register(typeof(OrderPlacedEvent), "ConcreteConsumer", typeof(object));

        IIntegrationEvent evt = MakeEvent(); // static type = IIntegrationEvent
        await _sut.PublishAsync(evt);

        await _inboxStore.Received(1).AddAsync(
            Arg.Is<InboxMessage>(m =>
                m.ConsumerType == "ConcreteConsumer" &&
                m.EventType == typeof(OrderPlacedEvent).AssemblyQualifiedName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenDuplicateInboxRow_SilentNoOp()
    {
        // EfInboxStore absorbs DbUpdateException. Core's PublishAsync must not throw
        // when AddAsync completes normally — the abstraction swallows duplicates.
        _registry.Register(typeof(OrderPlacedEvent), "Consumer", typeof(object));
        _inboxStore.AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask); // absorbs silently

        var evt = MakeEvent();
        await Should.NotThrowAsync(async () => await _sut.PublishAsync(evt));
    }

    private static OrderPlacedEvent MakeEvent() => new(
        MessageId: MessageId.New(),
        TenantId: "tenant-test");
}

internal sealed record OrderPlacedEvent(
    MessageId MessageId,
    string TenantId) : IIntegrationEvent
{
    public CorrelationId? CorrelationId => null;
    public CausationId? CausationId => null;
    public DateTimeOffset OccurredOnUtc { get; } = DateTimeOffset.UtcNow;
}
