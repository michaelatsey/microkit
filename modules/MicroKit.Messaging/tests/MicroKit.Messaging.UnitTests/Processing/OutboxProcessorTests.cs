namespace MicroKit.Messaging.UnitTests.Processing;

public sealed class OutboxProcessorTests
{
    private static OutboxProcessorOptions DefaultOptions => new()
    {
        BatchSize = 10,
        PollingInterval = TimeSpan.FromMilliseconds(10),
        MaxRetries = 3,
        LockDuration = TimeSpan.FromMinutes(1),
    };

    // ---------------------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenMessagePending_AcquiresLeaseAndDispatches()
    {
        var store = Substitute.For<IOutboxProcessorStore>();
        var dispatcher = new RecordingDispatcher();
        var message = MakeMessage(retryCount: 0);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>([message]));
        store.AcquireLeaseAsync(message.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = Build(store, dispatcher);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.Received(1).AcquireLeaseAsync(message.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        dispatcher.Dispatched.ShouldContain(m => m.Id == message.Id);
    }

    [Fact]
    public async Task ProcessBatch_WhenDispatchSucceeds_MarksPublished()
    {
        var store = Substitute.For<IOutboxProcessorStore>();
        var dispatcher = new RecordingDispatcher();
        var message = MakeMessage(retryCount: 0);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>([message]));
        store.AcquireLeaseAsync(message.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = Build(store, dispatcher);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.Received(1).MarkPublishedAsync(message.Id, Arg.Any<CancellationToken>());
        await store.DidNotReceive().MarkFailedAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().DeadLetterAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // Retry / dead-letter
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenDispatchThrows_BelowMaxRetries_ResetsStatusToPendingAndIncrementsRetry()
    {
        var store = Substitute.For<IOutboxProcessorStore>();
        var dispatcher = new RecordingDispatcher { DispatchException = new InvalidOperationException("transient") };
        var message = MakeMessage(retryCount: 0);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>([message]));
        store.AcquireLeaseAsync(message.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = Build(store, dispatcher);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        // nextRetryCount = message.RetryCount + 1 = 1
        await store.Received(1).MarkFailedAsync(message.Id, Arg.Any<string>(), 1, Arg.Any<CancellationToken>());
        await store.DidNotReceive().DeadLetterAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().MarkPublishedAsync(Arg.Any<MessageId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_WhenDispatchThrows_AtMaxRetries_DeadLetters()
    {
        var store = Substitute.For<IOutboxProcessorStore>();
        var dispatcher = new RecordingDispatcher { DispatchException = new InvalidOperationException("permanent") };
        // retryCount = MaxRetries - 1 so nextRetryCount = MaxRetries → triggers dead-letter
        var message = MakeMessage(retryCount: DefaultOptions.MaxRetries - 1);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>([message]));
        store.AcquireLeaseAsync(message.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = Build(store, dispatcher);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.Received(1).DeadLetterAsync(message.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().MarkFailedAsync(Arg.Any<MessageId>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // Lease / batch edge cases
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenLeaseNotAcquired_SkipsMessage()
    {
        var store = Substitute.For<IOutboxProcessorStore>();
        var dispatcher = new RecordingDispatcher();
        var message = MakeMessage(retryCount: 0);

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>([message]));
        store.AcquireLeaseAsync(message.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var sut = Build(store, dispatcher);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        dispatcher.Dispatched.ShouldBeEmpty();
        await store.DidNotReceive().MarkPublishedAsync(Arg.Any<MessageId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_WhenBatchEmpty_DoesNothing()
    {
        var store = Substitute.For<IOutboxProcessorStore>();
        var dispatcher = new RecordingDispatcher();

        store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>([]));

        var sut = Build(store, dispatcher);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        dispatcher.Dispatched.ShouldBeEmpty();
        await store.DidNotReceive().AcquireLeaseAsync(Arg.Any<MessageId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static OutboxProcessor Build(
        IOutboxProcessorStore store,
        RecordingDispatcher dispatcher,
        OutboxProcessorOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxDispatcher>(dispatcher);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        return new OutboxProcessor(
            store,
            new TestExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()),
            options ?? DefaultOptions,
            NullLogger<OutboxProcessor>.Instance);
    }

    private static OutboxMessage MakeMessage(int retryCount) => new()
    {
        Id = MessageId.New(),
        TenantId = "tenant-1",
        EventType = "TestEvent",
        Payload = "{}",
        Status = OutboxMessageStatus.Pending,
        RetryCount = retryCount,
        OccurredOnUtc = DateTimeOffset.UtcNow,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        CorrelationId = CorrelationId.New(),
    };
}

// ---------------------------------------------------------------------------
// Fake IOutboxDispatcher
// ---------------------------------------------------------------------------

internal sealed class RecordingDispatcher : IOutboxDispatcher
{
    public List<OutboxMessage> Dispatched { get; } = [];
    public InvalidOperationException? DispatchException { get; set; }

    public ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
    {
        if (DispatchException is not null) throw DispatchException;
        Dispatched.Add(message);
        return ValueTask.CompletedTask;
    }
}
