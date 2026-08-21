// NSubstitute ValueTask setup chains (.Returns on the method-call result) are intentional;
// they are not a ValueTask misuse — CA2012 is suppressed for this test file.
#pragma warning disable CA2012
namespace MicroKit.Messaging.MediatR.UnitTests;

/// <summary>
/// The glue's contribution to domain-event dispatch: P3 (notification mapping) and P4 (batched
/// outbox write). P1 (drain) and P2 (handler dispatch) belong to the core orchestrator and are
/// covered in MicroKit.MediatR — this sink never sees them (ADR-MEDIATR-014).
/// </summary>
/// <remarks>
/// Every "wrote nothing" assertion names <c>AddBatchAsync</c>, which is the method this sink
/// actually calls. Naming <c>AddAsync</c> — as these tests previously did — makes the assertion
/// unfalsifiable, because <c>DidNotReceive()</c> on a method the subject never invokes is green no
/// matter what the subject does. See L0-FINDINGS.md Finding #12.
/// </remarks>
public sealed class OutboxDomainEventSinkTests
{
    private readonly IDomainEventNotificationFactory _factory = Substitute.For<IDomainEventNotificationFactory>();
    private readonly IMessageSerializer _serializer = Substitute.For<IMessageSerializer>();
    private readonly IOutboxWriter _outboxWriter = Substitute.For<IOutboxWriter>();
    private readonly IExecutionContext _ctx = Substitute.For<IExecutionContext>();

    private readonly OutboxDomainEventSink _sut;

    public OutboxDomainEventSinkTests()
    {
        _serializer.Serialize(Arg.Any<object>()).Returns("{}");
        _sut = new OutboxDomainEventSink(
            _factory, new OutboxMessageFactory(_serializer), _outboxWriter, _ctx);
    }

    [Fact]
    public async Task ReceiveAsync_WhenNoEventMapsToANotification_WritesNothing()
    {
        var domainEvent = new TestDomainEvent();
        _factory.Create(domainEvent).Returns((IDomainEventNotification<IDomainEvent>?)null);

        await _sut.ReceiveAsync([domainEvent]);

        await _outboxWriter.DidNotReceive()
            .AddBatchAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAsync_WhenSomeEventsMap_WritesOnlyTheMappedOnesInOneBatch()
    {
        var mapped = new TestDomainEvent();
        var unmapped = new TestDomainEvent();
        _factory.Create(mapped).Returns(new TestNotification(mapped));
        _factory.Create(unmapped).Returns((IDomainEventNotification<IDomainEvent>?)null);

        IReadOnlyList<OutboxMessage>? captured = null;
        _outboxWriter
            .AddBatchAsync(Arg.Do<IReadOnlyList<OutboxMessage>>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        await _sut.ReceiveAsync([mapped, unmapped]);

        // One batch, one round-trip, only the mapped event in it (ADR-MSG-011).
        await _outboxWriter.Received(1)
            .AddBatchAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        captured!.Count.ShouldBe(1);
        captured[0].Id.ShouldBe(MessageId.From(mapped.EventId));
    }

    [Fact]
    public async Task ReceiveAsync_OutboxPayload_IsSerializedNotification()
    {
        var domainEvent = new TestDomainEvent();
        var notification = new TestNotification(domainEvent);
        _factory.Create(domainEvent).Returns(notification);

        await _sut.ReceiveAsync([domainEvent]);

        // Payload is the serialized NOTIFICATION (not the raw domain event).
        _serializer.Received(1).Serialize(notification);
    }

    [Fact]
    public async Task ReceiveAsync_MessageId_EqualsDomainEventEventId()
    {
        var domainEvent = new TestDomainEvent();
        _factory.Create(domainEvent).Returns(new TestNotification(domainEvent));

        IReadOnlyList<OutboxMessage>? captured = null;
        _outboxWriter
            .AddBatchAsync(Arg.Do<IReadOnlyList<OutboxMessage>>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        await _sut.ReceiveAsync([domainEvent]);

        captured.ShouldNotBeNull();
        captured![0].Id.ShouldBe(MessageId.From(domainEvent.EventId));
    }

    [Fact]
    public async Task ReceiveAsync_OccurredOnUtc_EqualsDomainEventOccurredAt()
    {
        var domainEvent = new TestDomainEvent();
        _factory.Create(domainEvent).Returns(new TestNotification(domainEvent));

        IReadOnlyList<OutboxMessage>? captured = null;
        _outboxWriter
            .AddBatchAsync(Arg.Do<IReadOnlyList<OutboxMessage>>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        await _sut.ReceiveAsync([domainEvent]);

        captured.ShouldNotBeNull();
        captured![0].OccurredOnUtc.ShouldBe(domainEvent.OccurredAt);
    }

    [Fact]
    public async Task ReceiveAsync_TransitMetadata_SourcedFromExecutionContext()
    {
        const string TenantId = "tenant-1";
        var correlationGuid = Guid.NewGuid();
        var causationGuid = Guid.NewGuid();

        _ctx.TenantId.Returns(TenantId);
        _ctx.CorrelationId.Returns(correlationGuid.ToString());
        _ctx.CausationId.Returns(causationGuid.ToString());

        var domainEvent = new TestDomainEvent();
        _factory.Create(domainEvent).Returns(new TestNotification(domainEvent));

        IReadOnlyList<OutboxMessage>? captured = null;
        _outboxWriter
            .AddBatchAsync(Arg.Do<IReadOnlyList<OutboxMessage>>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        await _sut.ReceiveAsync([domainEvent]);

        captured.ShouldNotBeNull();
        var message = captured![0];
        message.TenantId.ShouldBe(TenantId);
        message.CorrelationId.ShouldBe(CorrelationId.From(correlationGuid));
        message.CausationId.ShouldBe(CausationId.From(causationGuid));
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
