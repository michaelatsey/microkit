using MicroKit.Messaging.Serialization;

namespace MicroKit.Messaging.UnitTests.Processing;

public sealed class InboxProcessorTests
{
    private static InboxProcessorOptions DefaultOptions => new()
    {
        BatchSize = 10,
        PollingInterval = TimeSpan.FromMilliseconds(10),
        LeaseDuration = TimeSpan.FromMinutes(1),
        MaxRetries = 3,
    };

    // ---------------------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenHandlerSucceeds_MarksProcessed()
    {
        var store = Substitute.For<IInboxStore>();
        var handler = new RecordingInboxHandler();
        var (registry, consumerType) = MakeRegistry(handler);
        var serializer = new SystemTextJsonMessageSerializer();
        var evt = MakeEvent();
        var message = MakeMessage(evt, consumerType, serializer);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<InboxMessage>>([message]));

        var sut = Build(store, registry, serializer, handler);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.Received(1).MarkProcessingAsync(evt.MessageId, consumerType, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        handler.InvocationCount.ShouldBe(1);
        await store.Received(1).MarkProcessedAsync(evt.MessageId, consumerType, Arg.Any<CancellationToken>());
        await store.DidNotReceive().MarkFailedAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // Retry / dead-letter — handler exception (ADR-MSG-003 symmetry)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenHandlerThrows_BelowMaxRetries_ResetsToReceivedAndIncrementsRetry()
    {
        var store = Substitute.For<IInboxStore>();
        var handler = new RecordingInboxHandler { ThrowOnHandle = new InvalidOperationException("handler error") };
        var (registry, consumerType) = MakeRegistry(handler);
        var serializer = new SystemTextJsonMessageSerializer();
        var evt = MakeEvent();
        var message = MakeMessage(evt, consumerType, serializer, retryCount: 0);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<InboxMessage>>([message]));

        var sut = Build(store, registry, serializer, handler);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        // nextRetryCount = 1 (below MaxRetries=3) → MarkFailed (resets to Received + back-off)
        await store.Received(1).MarkFailedAsync(evt.MessageId, consumerType, Arg.Any<string>(), 1, Arg.Any<CancellationToken>());
        await store.DidNotReceive().DeadLetterAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().MarkProcessedAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_WhenHandlerThrows_AtMaxRetries_DeadLetters()
    {
        var store = Substitute.For<IInboxStore>();
        var handler = new RecordingInboxHandler { ThrowOnHandle = new InvalidOperationException("permanent") };
        var (registry, consumerType) = MakeRegistry(handler);
        var serializer = new SystemTextJsonMessageSerializer();
        var evt = MakeEvent();
        // retryCount = MaxRetries - 1 so nextRetryCount = MaxRetries → dead-letter
        var message = MakeMessage(evt, consumerType, serializer, retryCount: DefaultOptions.MaxRetries - 1);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<InboxMessage>>([message]));

        var sut = Build(store, registry, serializer, handler);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.Received(1).DeadLetterAsync(evt.MessageId, consumerType, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().MarkFailedAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // Deserialization failure (ADR-MSG-003 symmetry)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenDeserializationFails_BelowMaxRetries_ResetsToReceived()
    {
        var store = Substitute.For<IInboxStore>();
        var handler = new RecordingInboxHandler();
        var (registry, consumerType) = MakeRegistry(handler);
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<string>(), Arg.Any<string>()).Returns((object?)null);
        var evt = MakeEvent();
        var message = MakeMessage(evt, consumerType, new SystemTextJsonMessageSerializer(), retryCount: 0);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<InboxMessage>>([message]));

        var sut = Build(store, registry, serializer, handler);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.Received(1).MarkFailedAsync(evt.MessageId, consumerType, Arg.Any<string>(), 1, Arg.Any<CancellationToken>());
        await store.DidNotReceive().DeadLetterAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        handler.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task ProcessBatch_WhenDeserializationFails_AtMaxRetries_DeadLetters()
    {
        var store = Substitute.For<IInboxStore>();
        var handler = new RecordingInboxHandler();
        var (registry, consumerType) = MakeRegistry(handler);
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<string>(), Arg.Any<string>()).Returns((object?)null);
        var evt = MakeEvent();
        var message = MakeMessage(evt, consumerType, new SystemTextJsonMessageSerializer(), retryCount: DefaultOptions.MaxRetries - 1);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<InboxMessage>>([message]));

        var sut = Build(store, registry, serializer, handler);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.Received(1).DeadLetterAsync(evt.MessageId, consumerType, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().MarkFailedAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // Unknown consumer type (ADR-MSG-003 symmetry)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenConsumerNotRegistered_BelowMaxRetries_ResetsToReceived()
    {
        var store = Substitute.For<IInboxStore>();
        var handler = new RecordingInboxHandler();
        var serializer = new SystemTextJsonMessageSerializer();
        var registry = new MessageHandlerRegistry(); // empty — no handlers
        var evt = MakeEvent();
        const string unknownConsumer = "Unknown.Consumer, NonExistentAssembly";
        var message = MakeMessage(evt, unknownConsumer, serializer, retryCount: 0);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<InboxMessage>>([message]));

        var sut = Build(store, registry, serializer, handler);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.Received(1).MarkFailedAsync(evt.MessageId, unknownConsumer, Arg.Any<string>(), 1, Arg.Any<CancellationToken>());
        await store.DidNotReceive().DeadLetterAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        handler.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task ProcessBatch_WhenConsumerNotRegistered_AtMaxRetries_DeadLetters()
    {
        var store = Substitute.For<IInboxStore>();
        var handler = new RecordingInboxHandler();
        var serializer = new SystemTextJsonMessageSerializer();
        var registry = new MessageHandlerRegistry(); // empty
        var evt = MakeEvent();
        const string unknownConsumer = "Unknown.Consumer, NonExistentAssembly";
        var message = MakeMessage(evt, unknownConsumer, serializer, retryCount: DefaultOptions.MaxRetries - 1);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<InboxMessage>>([message]));

        var sut = Build(store, registry, serializer, handler);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.Received(1).DeadLetterAsync(evt.MessageId, unknownConsumer, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().MarkFailedAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // Drain contract — no ingestion calls
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenBatchEmpty_DoesNothing()
    {
        var store = Substitute.For<IInboxStore>();
        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<InboxMessage>>([]));

        var sut = Build(store, new MessageHandlerRegistry(), new SystemTextJsonMessageSerializer(), new RecordingInboxHandler());
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.DidNotReceive().MarkProcessingAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().ExistsAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_DoesNotCallExistsAsync()
    {
        var store = Substitute.For<IInboxStore>();
        var handler = new RecordingInboxHandler();
        var (registry, consumerType) = MakeRegistry(handler);
        var serializer = new SystemTextJsonMessageSerializer();
        var evt = MakeEvent();
        var message = MakeMessage(evt, consumerType, serializer);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<InboxMessage>>([message]));

        var sut = Build(store, registry, serializer, handler);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.DidNotReceive().ExistsAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static InboxProcessor Build(
        IInboxStore store,
        MessageHandlerRegistry registry,
        IMessageSerializer serializer,
        RecordingInboxHandler handler,
        InboxProcessorOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        return new InboxProcessor(
            store,
            registry,
            serializer,
            new TestExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()),
            options ?? DefaultOptions,
            NullLogger<InboxProcessor>.Instance);
    }

    private static (MessageHandlerRegistry registry, string consumerType) MakeRegistry(RecordingInboxHandler handler)
    {
        var registry = new MessageHandlerRegistry();
        var consumerType = typeof(RecordingInboxHandler).AssemblyQualifiedName!;
        registry.Register(typeof(InboxTestEvent), consumerType, typeof(RecordingInboxHandler));
        return (registry, consumerType);
    }

    private static InboxTestEvent MakeEvent() => new(
        MessageId: MessageId.New(),
        TenantId: "tenant-test");

    private static InboxMessage MakeMessage(
        InboxTestEvent evt,
        string consumerType,
        IMessageSerializer serializer,
        int retryCount = 0) => new()
    {
        MessageId = evt.MessageId,
        ConsumerType = consumerType,
        TenantId = evt.TenantId,
        EventType = typeof(InboxTestEvent).AssemblyQualifiedName!,
        Payload = serializer.Serialize(evt),
        Status = InboxMessageStatus.Received,
        RetryCount = retryCount,
        ReceivedAtUtc = DateTimeOffset.UtcNow,
    };
}

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

internal sealed record InboxTestEvent(MessageId MessageId, string TenantId) : IIntegrationEvent
{
    public CorrelationId? CorrelationId => null;
    public CausationId? CausationId => null;
    public DateTimeOffset OccurredOnUtc { get; } = DateTimeOffset.UtcNow;
}

internal sealed class RecordingInboxHandler : IMessageHandler<InboxTestEvent>
{
    private int _invocationCount;
    public int InvocationCount => _invocationCount;
    public InvalidOperationException? ThrowOnHandle { get; set; }

    public ValueTask HandleAsync(InboxTestEvent evt, CancellationToken ct = default)
    {
        System.Threading.Interlocked.Increment(ref _invocationCount);
        if (ThrowOnHandle is not null) throw ThrowOnHandle;
        return ValueTask.CompletedTask;
    }
}
