namespace MicroKit.Messaging.UnitTests.Dispatch;

using MicroKit.Messaging.Dispatch;

/// <summary>
/// The in-process fan-out: one inbox row per registered consumer, every field taken from the
/// outbox row.
/// </summary>
/// <remarks>
/// These assertions moved here from <c>InProcessMessagePublisherTests</c> when the fan-out moved
/// out of <c>IMessagePublisher</c> (ADR-MSG-018). The behaviour is unchanged; the source of the
/// message metadata is not, and that is what the first two tests below pin.
/// </remarks>
public sealed class InProcessIntegrationDispatcherTests
{
    private readonly IMessageSerializer _serializer = Substitute.For<IMessageSerializer>();
    private readonly IInboxWriter _inboxWriter = Substitute.For<IInboxWriter>();
    private readonly MessageHandlerRegistry _registry = new();
    private readonly List<InboxMessage> _written = [];

    public InProcessIntegrationDispatcherTests()
        => _inboxWriter
            .AddAsync(Arg.Do<InboxMessage>(_written.Add), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(InboxWriteResult.Added));

    private InProcessIntegrationDispatcher Build()
        => new(_serializer, _registry, _inboxWriter, new InboxMetrics(),
               NullLogger<InProcessIntegrationDispatcher>.Instance);

    /// <summary>
    /// The dedup key is the outbox row's identity, not the event's.
    /// </summary>
    /// <remarks>
    /// It has to be: the inbox unique index is what absorbs a redelivery, and a redelivery
    /// re-dispatches the same row. Reading the id off the deserialized event only ever survived
    /// that by accident — the same payload happens to deserialize to the same value, but nothing
    /// guaranteed it. Here the event carries a deliberately different id, so the assertion fails
    /// if anyone reintroduces the old source.
    /// </remarks>
    [Fact]
    public async Task DispatchAsync_TakesTheDedupKeyFromTheOutboxRow_NotTheEvent()
    {
        var evt = new FanoutTestEvent(MessageId.New());
        var message = MakeOutboxMessage(evt);
        _serializer.Deserialize(message.Payload, message.EventType).Returns(evt);
        RegisterConsumer("consumer-a");

        await Build().DispatchAsync(message);

        var row = _written.ShouldHaveSingleItem();
        row.MessageId.ShouldBe(message.Id);
        row.MessageId.ShouldNotBe(evt.OwnIdentity);
    }

    [Fact]
    public async Task DispatchAsync_TakesEveryFieldFromTheOutboxRow()
    {
        var evt = new FanoutTestEvent(MessageId.New());
        var message = MakeOutboxMessage(evt);
        _serializer.Deserialize(message.Payload, message.EventType).Returns(evt);
        RegisterConsumer("consumer-a");

        await Build().DispatchAsync(message);

        var row = _written.ShouldHaveSingleItem();
        row.TenantId.ShouldBe(message.TenantId);
        row.EventType.ShouldBe(message.EventType);
        row.Payload.ShouldBe(message.Payload);
        row.CorrelationId.ShouldBe(message.CorrelationId);
        row.CausationId.ShouldBe(message.CausationId);
        row.Status.ShouldBe(InboxMessageStatus.Received);
    }

    [Fact]
    public async Task DispatchAsync_WhenMultipleSubscribersRegistered_WritesOneRowPerConsumer()
    {
        var evt = new FanoutTestEvent(MessageId.New());
        var message = MakeOutboxMessage(evt);
        _serializer.Deserialize(message.Payload, message.EventType).Returns(evt);
        RegisterConsumer("consumer-a");
        RegisterConsumer("consumer-b");

        await Build().DispatchAsync(message);

        _written.Count.ShouldBe(2);
        _written.Select(r => r.ConsumerType).ShouldBe(["consumer-a", "consumer-b"], ignoreOrder: true);
        _written.Select(r => r.RowId).Distinct().Count().ShouldBe(2, "every row carries its own RowId");
    }

    [Fact]
    public async Task DispatchAsync_WhenNoSubscriberRegistered_WritesNothingAndDoesNotThrow()
    {
        var evt = new FanoutTestEvent(MessageId.New());
        var message = MakeOutboxMessage(evt);
        _serializer.Deserialize(message.Payload, message.EventType).Returns(evt);

        await Build().DispatchAsync(message);

        // Valid in a multi-service deployment where this event has no local consumer.
        _written.ShouldBeEmpty();
    }

    /// <summary>
    /// The partial-loss fix: a duplicate for one consumer must not stop the others getting a row.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_WhenOneConsumerIsAlreadyPresent_StillWritesTheOthers()
    {
        var evt = new FanoutTestEvent(MessageId.New());
        var message = MakeOutboxMessage(evt);
        _serializer.Deserialize(message.Payload, message.EventType).Returns(evt);
        RegisterConsumer("consumer-a");
        RegisterConsumer("consumer-b");

        // Override the RETURN sequence only. Re-specifying Arg.Do here would stack a second
        // recording callback on the one the constructor set up, and every write would be
        // recorded twice.
        _inboxWriter
            .AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => ValueTask.FromResult(InboxWriteResult.AlreadyPresent),
                _ => ValueTask.FromResult(InboxWriteResult.Added));

        await Build().DispatchAsync(message);

        _written.Count.ShouldBe(2, "a redelivery for one consumer is a skip, not an early return");
    }

    [Fact]
    public async Task DispatchAsync_WhenDeserializeReturnsNull_ThrowsOutboxPayloadException()
    {
        var message = MakeOutboxMessage(new FanoutTestEvent(MessageId.New()), eventType: "SomeType");
        _serializer.Deserialize(Arg.Any<string>(), Arg.Any<string>()).Returns((object?)null);

        var ex = await Should.ThrowAsync<OutboxPayloadException>(
            async () => await Build().DispatchAsync(message));

        ex.Message.ShouldContain("SomeType");
    }

    [Fact]
    public async Task DispatchAsync_WhenTheWriteFailsForReal_Propagates()
    {
        var evt = new FanoutTestEvent(MessageId.New());
        var message = MakeOutboxMessage(evt);
        _serializer.Deserialize(message.Payload, message.EventType).Returns(evt);
        RegisterConsumer("consumer-a");

        _inboxWriter.AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException<InboxWriteResult>(new InvalidOperationException("boom")));

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await Build().DispatchAsync(message));
    }

    /// <summary>
    /// The consumer lookup uses the RUNTIME type. The static type at the call site is always the
    /// interface, so a lookup on the declared type would find nothing and silently drop the event.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ResolvesConsumersByRuntimeType_NotTheStaticType()
    {
        var concrete = new FanoutTestEvent(MessageId.New());
        IIntegrationEvent asInterface = concrete;

        var message = MakeOutboxMessage(concrete);
        _serializer.Deserialize(Arg.Any<string>(), Arg.Any<string>()).Returns(asInterface);
        RegisterConsumer("consumer-a");

        await Build().DispatchAsync(message);

        _written.ShouldHaveSingleItem().ConsumerType.ShouldBe("consumer-a");
    }

    private void RegisterConsumer(string consumerType)
        => _registry.Register(typeof(FanoutTestEvent), consumerType, typeof(FanoutTestHandler));

    private static OutboxMessage MakeOutboxMessage(FanoutTestEvent evt, string? eventType = null) => new()
    {
        Id = MessageId.New(),
        TenantId = "tenant-1",
        EventType = eventType ?? evt.GetType().AssemblyQualifiedName!,
        Payload = """{"marker":1}""",
        Status = OutboxMessageStatus.Processing,
        RetryCount = 0,
        OccurredOnUtc = DateTimeOffset.UnixEpoch,
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        CorrelationId = CorrelationId.New(),
        CausationId = CausationId.New(),
    };
}

/// <summary>
/// A bare marker event (ADR-MSG-018). <c>OwnIdentity</c> is this record's own property, not a
/// contract member — it exists so a test can prove the dispatcher ignores it.
/// </summary>
internal sealed record FanoutTestEvent(MessageId OwnIdentity) : IIntegrationEvent;

internal sealed class FanoutTestHandler : IMessageHandler<FanoutTestEvent>
{
    public ValueTask HandleAsync(FanoutTestEvent evt, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
