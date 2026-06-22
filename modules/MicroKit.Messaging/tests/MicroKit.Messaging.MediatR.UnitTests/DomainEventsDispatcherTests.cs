namespace MicroKit.Messaging.MediatR.UnitTests;

public sealed class DomainEventsDispatcherTests
{
    private readonly IDomainEventsProvider _eventsProvider = Substitute.For<IDomainEventsProvider>();
    private readonly IDomainEventHandlerDispatcher _handlerDispatcher = Substitute.For<IDomainEventHandlerDispatcher>();
    private readonly IDomainEventNotificationFactory _factory = Substitute.For<IDomainEventNotificationFactory>();
    private readonly IMessageSerializer _serializer = Substitute.For<IMessageSerializer>();
    private readonly IOutboxWriter _outboxWriter = Substitute.For<IOutboxWriter>();
    private readonly IExecutionContext _ctx = Substitute.For<IExecutionContext>();

    private readonly DomainEventsDispatcher _sut;

    public DomainEventsDispatcherTests()
    {
        _serializer.Serialize(Arg.Any<object>()).Returns("{}");
        var outboxFactory = new OutboxMessageFactory(_serializer);
        _sut = new DomainEventsDispatcher(
            _eventsProvider, _handlerDispatcher, _factory, outboxFactory, _outboxWriter, _ctx);
    }

    [Fact]
    public async Task DispatchEventsAsync_WhenNoDomainEvents_WritesNoOutbox()
    {
        _eventsProvider.DrainDomainEvents().Returns([]);

        await _sut.DispatchEventsAsync();

        await _outboxWriter.DidNotReceive().AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchEventsAsync_WhenDomainEventHasNoNotification_StillInvokesHandlerDispatcher()
    {
        var domainEvent = new TestDomainEvent();
        _eventsProvider.DrainDomainEvents().Returns([domainEvent]);
        _factory.Create(domainEvent).Returns((IDomainEventNotification<IDomainEvent>?)null);

        await _sut.DispatchEventsAsync();

        await _handlerDispatcher.Received(1).DispatchAsync(domainEvent, Arg.Any<CancellationToken>());
        await _outboxWriter.DidNotReceive().AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchEventsAsync_WhenNotificationFactoryReturnsNull_SkipsOutbox()
    {
        var domainEvent = new TestDomainEvent();
        _eventsProvider.DrainDomainEvents().Returns([domainEvent]);
        _factory.Create(domainEvent).Returns((IDomainEventNotification<IDomainEvent>?)null);

        await _sut.DispatchEventsAsync();

        // P2 fires for every domain event regardless of whether a notification exists.
        await _handlerDispatcher.Received(1).DispatchAsync(domainEvent, Arg.Any<CancellationToken>());
        // P4 is skipped because the notification factory returned null.
        await _outboxWriter.DidNotReceive().AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchEventsAsync_WhenIEvent_InvokesHandlerDispatcher()
    {
        var domainEvent = new TestDomainEvent();
        var notification = new TestNotification(domainEvent);
        _eventsProvider.DrainDomainEvents().Returns([domainEvent]);
        _factory.Create(domainEvent).Returns(notification);

        await _sut.DispatchEventsAsync();

        await _handlerDispatcher.Received(1).DispatchAsync(domainEvent, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchEventsAsync_OutboxPayload_IsSerializedNotification()
    {
        var domainEvent = new TestDomainEvent();
        var notification = new TestNotification(domainEvent);
        _eventsProvider.DrainDomainEvents().Returns([domainEvent]);
        _factory.Create(domainEvent).Returns(notification);

        await _sut.DispatchEventsAsync();

        // Payload is the serialized NOTIFICATION (not the raw domain event).
        _serializer.Received(1).Serialize(notification);
    }

    [Fact]
    public async Task DispatchEventsAsync_MessageId_EqualsDomainEventEventId()
    {
        var domainEvent = new TestDomainEvent();
        var notification = new TestNotification(domainEvent);
        _eventsProvider.DrainDomainEvents().Returns([domainEvent]);
        _factory.Create(domainEvent).Returns(notification);

        OutboxMessage? captured = null;
        await _outboxWriter.AddAsync(Arg.Do<OutboxMessage>(m => captured = m), Arg.Any<CancellationToken>());

        await _sut.DispatchEventsAsync();

        captured.ShouldNotBeNull();
        captured!.Id.ShouldBe(MessageId.From(domainEvent.EventId));
    }

    [Fact]
    public async Task DispatchEventsAsync_OccurredOnUtc_EqualsDomainEventOccurredAt()
    {
        var domainEvent = new TestDomainEvent();
        var notification = new TestNotification(domainEvent);
        _eventsProvider.DrainDomainEvents().Returns([domainEvent]);
        _factory.Create(domainEvent).Returns(notification);

        OutboxMessage? captured = null;
        await _outboxWriter.AddAsync(Arg.Do<OutboxMessage>(m => captured = m), Arg.Any<CancellationToken>());

        await _sut.DispatchEventsAsync();

        captured!.OccurredOnUtc.ShouldBe(domainEvent.OccurredAt);
    }

    [Fact]
    public async Task DispatchEventsAsync_TransitMetadata_SourcedFromExecutionContext()
    {
        var tenantId = "tenant-1";
        var correlationGuid = Guid.NewGuid();
        var causationGuid = Guid.NewGuid();

        _ctx.TenantId.Returns(tenantId);
        _ctx.CorrelationId.Returns(correlationGuid.ToString());
        _ctx.CausationId.Returns(causationGuid.ToString());

        var domainEvent = new TestDomainEvent();
        var notification = new TestNotification(domainEvent);
        _eventsProvider.DrainDomainEvents().Returns([domainEvent]);
        _factory.Create(domainEvent).Returns(notification);

        OutboxMessage? captured = null;
        await _outboxWriter.AddAsync(Arg.Do<OutboxMessage>(m => captured = m), Arg.Any<CancellationToken>());

        await _sut.DispatchEventsAsync();

        captured!.TenantId.ShouldBe(tenantId);
        captured.CorrelationId.ShouldBe(CorrelationId.From(correlationGuid));
        captured.CausationId.ShouldBe(CausationId.From(causationGuid));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class TestDomainEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class TestNotification(TestDomainEvent domainEvent)
        : IDomainEventNotification<TestDomainEvent>
    {
        public TestDomainEvent DomainEvent { get; } = domainEvent;
    }
}
