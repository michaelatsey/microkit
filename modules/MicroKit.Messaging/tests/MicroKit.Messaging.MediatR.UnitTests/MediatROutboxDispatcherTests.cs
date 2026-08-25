namespace MicroKit.Messaging.MediatR.UnitTests;

/// <summary>
/// The routing contract of <c>MediatROutboxDispatcher</c>: it decides on
/// <see cref="OutboxMessage.MessageKind"/>, publishes notifications in process, and delegates
/// everything else inward without ever deserializing it (ADR-MSG-019).
/// </summary>
public sealed class MediatROutboxDispatcherTests
{
    private readonly RecordingDispatcher _inner = new();
    private readonly RecordingPublisher _publisher = new();

    // withInner: false is the notification-only composition — the keyed lookup found no standard
    // dispatcher. It is a flag rather than a nullable parameter on purpose: `inner ?? _inner` would
    // silently substitute the recording inner for a caller passing null, and every no-inner test
    // would then assert the opposite of its name.
    private MediatROutboxDispatcher Build(
        object? deserializesTo = null,
        bool withInner = true,
        RecordingSerializer? serializer = null)
        => new(withInner ? _inner : null,
               serializer ?? new RecordingSerializer(deserializesTo),
               _publisher,
               NullLogger<MediatROutboxDispatcher>.Instance);

    private static OutboxMessage MakeOutboxMessage(
        MessageKind kind = MessageKind.Notification,
        string eventType = "SomeType",
        string payload = "{}")
        => new()
        {
            Id = MessageId.New(),
            MessageKind = kind,
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
    public async Task DispatchAsync_WhenKindIsNotification_PublishesViaMediatR()
    {
        var notification = new FakeNotification();
        var message = MakeOutboxMessage(MessageKind.Notification);

        await Build(deserializesTo: notification).DispatchAsync(message);

        _publisher.Published.ShouldHaveSingleItem().ShouldBeSameAs(notification);
        _inner.Dispatched.ShouldBeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_WhenKindIsContract_DelegatesToTheInner()
    {
        var message = MakeOutboxMessage(MessageKind.Contract);

        await Build().DispatchAsync(message);

        _inner.Dispatched.ShouldHaveSingleItem().ShouldBeSameAs(message);
        _publisher.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_WhenKindIsContract_NeverTouchesTheSerializer()
    {
        // The discipline this class exists to keep. Deserializing a contract row would make
        // dispatch depend on the producer's type graph — exactly what does not cross a process
        // boundary — reinstating in front of TransportOutboxDispatcher the coupling that class was
        // built to avoid, while leaving it looking correct.
        //
        // A RECORDING FAKE, not DidNotReceive(): a negative assertion against a mock only holds if
        // it names the method the subject actually calls, so DidNotReceive().Deserialize(...) stays
        // green if the routing path starts calling Serialize instead. An empty call log cannot.
        var serializer = new RecordingSerializer(new FakeNotification());

        await Build(serializer: serializer).DispatchAsync(MakeOutboxMessage(MessageKind.Contract));

        serializer.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_WhenKindIsContract_AndPayloadIsANotification_StillDelegates()
    {
        // The test that kills the previous implementation. It routed on `payload is INotification`,
        // so a contract row carrying a payload that happens to implement INotification would have
        // been published in process instead of sent — and every other test in this file would still
        // have passed. The column decides; the payload does not get a vote.
        var message = MakeOutboxMessage(MessageKind.Contract);

        await Build(deserializesTo: new FakeNotification()).DispatchAsync(message);

        _inner.Dispatched.ShouldHaveSingleItem().ShouldBeSameAs(message);
        _publisher.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_WhenKindIsNotification_AndPayloadIsNotANotification_ThrowsOutboxPayloadException()
    {
        // The row declares a nature its payload cannot have — a staging defect written into a
        // persisted row, permanent in the same sense as an unresolvable EventType. It must not be
        // delegated inward, where the inner would answer a Notification kind with a configuration
        // fault and tell the operator to install a package they already have.
        var message = MakeOutboxMessage(MessageKind.Notification);

        var ex = await Should.ThrowAsync<OutboxPayloadException>(
            async () => await Build(deserializesTo: new FakeIntegrationEvent()).DispatchAsync(message));

        ex.Message.ShouldContain(nameof(FakeIntegrationEvent));
        _publisher.Published.ShouldBeEmpty();
        _inner.Dispatched.ShouldBeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_WhenKindIsNotification_AndPayloadDoesNotDeserialize_ThrowsOutboxPayloadException()
    {
        // Null means the EventType resolved to no CLR type or the JSON was malformed —
        // IMessageSerializer returns null rather than throwing for both. Permanent, so the
        // processor dead-letters on first sight instead of spending the retry budget re-reaching
        // the same verdict.
        var message = MakeOutboxMessage(MessageKind.Notification);

        var ex = await Should.ThrowAsync<OutboxPayloadException>(
            async () => await Build(deserializesTo: null).DispatchAsync(message));

        ex.Message.ShouldContain("SomeType");
        _publisher.Published.ShouldBeEmpty();
        _inner.Dispatched.ShouldBeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_WhenKindIsUnknown_DelegatesToTheInner()
    {
        // MessageKind is persisted as a string, so a row written by a later build can carry a value
        // this one cannot interpret. With an inner registered there is exactly one owner of that
        // verdict — TransportOutboxDispatcher's default arm — and this class is not it.
        var message = MakeOutboxMessage((MessageKind)999);

        await Build().DispatchAsync(message);

        _inner.Dispatched.ShouldHaveSingleItem().ShouldBeSameAs(message);
    }

    [Fact]
    public async Task DispatchAsync_WhenContractAndNoInner_ThrowsOutboxConfigurationException()
    {
        // The notification-only composition (ADR-MSG-019): no transport dispatcher registered, so
        // the keyed lookup yielded null. Configuration, not payload — the batch is released, no
        // retry budget is spent, the row survives and drains once a transport is deployed.
        var ex = await Should.ThrowAsync<OutboxConfigurationException>(
            async () => await Build(withInner: false).DispatchAsync(MakeOutboxMessage(MessageKind.Contract)));

        ex.Message.ShouldContain("AddTransportDispatcher()");
    }

    [Fact]
    public async Task DispatchAsync_WhenUnknownKindAndNoInner_IsReleasedRatherThanDeadLettered()
    {
        // Deliberately MORE conservative than TransportOutboxDispatcher, which dead-letters an
        // unknown kind. That one is a fully composed build, so an uninterpretable kind is permanent
        // for the deployment. Here the composition is by its own admission incomplete — the missing
        // registration may be the very package that understands the kind — so the reversible
        // verdict is the honest one. A row is not destroyed on the strength of a half-built host.
        await Should.ThrowAsync<OutboxConfigurationException>(
            async () => await Build(withInner: false).DispatchAsync(MakeOutboxMessage((MessageKind)999)));
    }

    [Fact]
    public async Task DispatchAsync_WhenNotificationAndNoInner_StillPublishes()
    {
        // The whole point of allowing a null inner: a notification-only host works.
        var notification = new FakeNotification();

        await Build(deserializesTo: notification, withInner: false)
            .DispatchAsync(MakeOutboxMessage(MessageKind.Notification));

        _publisher.Published.ShouldHaveSingleItem().ShouldBeSameAs(notification);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class RecordingSerializer(object? deserializesTo) : IMessageSerializer
    {
        public List<string> Calls { get; } = [];

        public string Serialize(object payload)
        {
            Calls.Add($"Serialize({payload.GetType().Name})");
            return "{}";
        }

        public object? Deserialize(string payload, string eventType)
        {
            Calls.Add($"Deserialize({eventType})");
            return deserializesTo;
        }
    }

    private sealed class RecordingDispatcher : IOutboxDispatcher
    {
        public List<OutboxMessage> Dispatched { get; } = [];

        public ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
        {
            Dispatched.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(
            TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Published.Add(notification!);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotification : INotification { }

    // A bare marker (ADR-MSG-018): the metadata members this used to declare existed only to
    // satisfy the interface, and the routing decorator never read them.
    private sealed record FakeIntegrationEvent : IIntegrationEvent;
}
