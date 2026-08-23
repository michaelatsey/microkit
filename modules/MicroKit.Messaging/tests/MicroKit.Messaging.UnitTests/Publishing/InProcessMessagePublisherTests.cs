namespace MicroKit.Messaging.UnitTests.Publishing;

using Microsoft.Extensions.Logging;

using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Registry;

public sealed class InProcessMessagePublisherTests : IDisposable
{
    private readonly MessageHandlerRegistry _registry = new();
    private readonly IInboxWriter _inboxWriter = Substitute.For<IInboxWriter>();
    private readonly IMessageSerializer _serializer = Substitute.For<IMessageSerializer>();
    private readonly InboxMetrics _metrics = new();
    private readonly ILogger<InProcessMessagePublisher> _logger =
        Substitute.For<ILogger<InProcessMessagePublisher>>();
    private readonly InProcessMessagePublisher _sut;

    public InProcessMessagePublisherTests()
    {
        _serializer.Serialize(Arg.Any<IIntegrationEvent>()).Returns("{}");
        _inboxWriter.AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(InboxWriteResult.Added));

        _sut = new InProcessMessagePublisher(_registry, _inboxWriter, _serializer, _metrics, _logger);
    }

    [Fact]
    public async Task PublishAsync_WhenSubscriberRegistered_WritesInboxRow()
    {
        _registry.Register(typeof(OrderPlacedEvent), "TestConsumer", typeof(object));
        var evt = MakeEvent();

        await _sut.PublishAsync(evt);

        await _inboxWriter.Received(1).AddAsync(
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

        await _inboxWriter.Received(2).AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenNoSubscriberRegistered_LogsWarningAndReturns()
    {
        var evt = MakeEvent();

        await _sut.PublishAsync(evt);

        await _inboxWriter.DidNotReceive().AddAsync(
            Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_UsesRuntimeTypeNotGenericTypeForLookup()
    {
        // Registry lookup must use evt.GetType(), not typeof(T). When called with
        // T=IIntegrationEvent (as InProcessIntegrationDispatcher does), the concrete type must
        // still resolve subscribers.
        _registry.Register(typeof(OrderPlacedEvent), "ConcreteConsumer", typeof(object));

        IIntegrationEvent evt = MakeEvent(); // static type = IIntegrationEvent
        await _sut.PublishAsync(evt);

        await _inboxWriter.Received(1).AddAsync(
            Arg.Is<InboxMessage>(m =>
                m.ConsumerType == "ConcreteConsumer" &&
                m.EventType == typeof(OrderPlacedEvent).AssemblyQualifiedName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_EveryRowCarriesItsOwnRowId()
    {
        // RowId is the surrogate primary key and is assigned by the writer, not the database.
        // Two rows for the same message must not share one, or the second insert violates the
        // primary key instead of the dedup index.
        _registry.Register(typeof(OrderPlacedEvent), "ConsumerA", typeof(object));
        _registry.Register(typeof(OrderPlacedEvent), "ConsumerB", typeof(object));

        var rowIds = new List<Guid>();
        _inboxWriter.AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                rowIds.Add(call.Arg<InboxMessage>().RowId);
                return ValueTask.FromResult(InboxWriteResult.Added);
            });

        await _sut.PublishAsync(MakeEvent());

        rowIds.Count.ShouldBe(2);
        rowIds.ShouldAllBe(id => id != Guid.Empty);
        rowIds.Distinct().Count().ShouldBe(2);
    }

    /// <summary>
    /// The actual defect fix. A redelivery is reported through the return value, and the
    /// publisher must return normally so the outbox marks the message Published. Previously the
    /// duplicate escaped as an exception, was classified a transient dispatch failure, and
    /// dead-lettered a message that had been delivered correctly on the first attempt.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenRowAlreadyPresent_ReturnsWithoutThrowing()
    {
        _registry.Register(typeof(OrderPlacedEvent), "Consumer", typeof(object));
        _inboxWriter.AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(InboxWriteResult.AlreadyPresent));

        await Should.NotThrowAsync(async () => await _sut.PublishAsync(MakeEvent()));
    }

    /// <summary>
    /// The second defect the fix exposes. One event fans out to one row per consumer, and a
    /// duplicate on consumer 2 used to abort the loop — so consumers 3..N never got their row at
    /// all, turning a partial redelivery into permanent loss for the later consumers.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenOneConsumerIsAlreadyPresent_StillWritesTheOthers()
    {
        _registry.Register(typeof(OrderPlacedEvent), "FirstConsumer", typeof(object));
        _registry.Register(typeof(OrderPlacedEvent), "SecondConsumer", typeof(object));
        _registry.Register(typeof(OrderPlacedEvent), "ThirdConsumer", typeof(object));

        var attempted = new List<string>();
        _inboxWriter.AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var consumer = call.Arg<InboxMessage>().ConsumerType;
                attempted.Add(consumer);

                return ValueTask.FromResult(consumer == "SecondConsumer"
                    ? InboxWriteResult.AlreadyPresent
                    : InboxWriteResult.Added);
            });

        await _sut.PublishAsync(MakeEvent());

        attempted.ShouldBe(["FirstConsumer", "SecondConsumer", "ThirdConsumer"]);
    }

    /// <summary>
    /// A genuine persistence fault must still propagate. Absorbing every failure would trade the
    /// spurious dead-letter for silent data loss, which is the worse defect.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenTheWriteFailsForReal_Propagates()
    {
        _registry.Register(typeof(OrderPlacedEvent), "Consumer", typeof(object));
        _inboxWriter.AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<InboxWriteResult>>(_ => throw new InvalidOperationException("connection lost"));

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await _sut.PublishAsync(MakeEvent()));

        ex.Message.ShouldContain("connection lost");
    }

    /// <summary>Disposes the meter the publisher's counters are registered on.</summary>
    public void Dispose() => _metrics.Dispose();

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
