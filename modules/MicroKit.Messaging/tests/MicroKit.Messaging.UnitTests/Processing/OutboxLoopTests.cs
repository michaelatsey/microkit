namespace MicroKit.Messaging.UnitTests.Processing;

public sealed class OutboxLoopTests
{
    // ---------------------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The loop hands the processor the configured batch size and the host's own token — what the
    /// coordinator it replaced did (#188).
    /// </summary>
    [Fact]
    public async Task RunAsync_PassesTheConfiguredBatchSizeAndTheHostsToken()
    {
        var seen = new System.Collections.Concurrent.ConcurrentQueue<(int Size, CancellationToken Token)>();
        var processor = Substitute.For<IOutboxProcessor>();
        processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                seen.Enqueue((ci.ArgAt<int>(0), ci.ArgAt<CancellationToken>(1)));
                return ValueTask.FromResult(OutboxBatchResult.Empty);
            });

        var services = new ServiceCollection();
        services.AddKeyedScoped<IOutboxProcessor>(LoopHarness.Store, (_, _) => processor);
        var provider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource();

        var run = new OutboxLoop(
            provider.GetRequiredService<IServiceScopeFactory>(),
            LoopHarness.Store,
            new OutboxProcessorOptions { BatchSize = 37, PollingInterval = TimeSpan.FromMilliseconds(5) },
            NullLogger<OutboxLoop>.Instance).RunAsync(cts.Token);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (seen.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await cts.CancelAsync();
        await run;

        seen.TryPeek(out var first).ShouldBeTrue();
        first.Size.ShouldBe(37);
        first.Token.ShouldBe(cts.Token);
    }

    /// <summary>
    /// Only a host stop ends the loop (#188 review): a disposed provider while the host runs is a
    /// failed cycle, backed off, and the loop keeps draining its store.
    /// </summary>
    [Fact]
    public async Task RunAsync_WhenTheProcessorThrowsObjectDisposedWhileTheHostRuns_KeepsRunning()
    {
        var calls = 0;
        var processor = Substitute.For<IOutboxProcessor>();
        processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref calls) == 1
                ? ValueTask.FromException<OutboxBatchResult>(new ObjectDisposedException(nameof(IServiceProvider)))
                : ValueTask.FromResult(OutboxBatchResult.Empty));

        var sut = BuildWorker(processor, out _, new OutboxProcessorOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(5),
            MaxPollingInterval = TimeSpan.FromMilliseconds(10),
        });

        await sut.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (Volatile.Read(ref calls) < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await sut.StopAsync(default);

        calls.ShouldBeGreaterThan(1, "the loop survived the failed cycle");
    }

    [Fact]
    public async Task RunAsync_CallsTheStoresProcessor()
    {
        var coordinator = Substitute.For<IOutboxProcessor>();
        var sut = BuildWorker(coordinator, out _);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await sut.StartAsync(cts.Token);
        await Task.Delay(200);
        await sut.StopAsync(default);

        await coordinator.Received().ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // Cancellation
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_StopsGracefully()
    {
        var coordinator = Substitute.For<IOutboxProcessor>();
        var sut = BuildWorker(coordinator, out _);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.NotThrowAsync(async () =>
        {
            await sut.StartAsync(cts.Token);
            await Task.Delay(200);
            await sut.StopAsync(default);
        });
    }

    // ---------------------------------------------------------------------------
    // Resilience — transient exception
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_WhenTheProcessorThrowsATransientException_LogsAndContinues()
    {
        var callCount = 0;
        var coordinator = Substitute.For<IOutboxProcessor>();
        coordinator
            .ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                System.Threading.Interlocked.Increment(ref callCount);
                return ValueTask.FromException<OutboxBatchResult>(new Exception("transient db error"));
            });

        var sut = BuildWorker(coordinator, out _);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await sut.StartAsync(cts.Token);
        await Task.Delay(300);
        await sut.StopAsync(default);

        // Worker must have continued after the exception (called the processor more than once)
        callCount.ShouldBeGreaterThan(1);
    }

    // ---------------------------------------------------------------------------
    // Resilience — misconfiguration stops worker
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A registered service whose construction throws is an activation failure (D11, #127): logged
    /// under its own event, with back-off, and the loop keeps running — never reported as an
    /// unregistered service, and never a reason to stop.
    /// </summary>
    [Fact]
    public async Task RunAsync_WhenTheProcessorCannotBeActivated_LogsAnActivationFailureAndKeepsRunning()
    {
        var attempts = 0;
        var calls = 0;
        var processor = Substitute.For<IOutboxProcessor>();
        processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(OutboxBatchResult.Empty);
            });

        var services = new ServiceCollection();
        services.AddKeyedScoped<IOutboxProcessor>(LoopHarness.Store, (_, _) =>
            Interlocked.Increment(ref attempts) == 1
                ? throw new InvalidOperationException("The store's constructor threw.")
                : processor);
        var provider = services.BuildServiceProvider();
        var logger = new CapturingLogger<OutboxLoop>();

        var sut = new LoopHarness(new OutboxLoop(
            provider.GetRequiredService<IServiceScopeFactory>(),
            LoopHarness.Store,
            new OutboxProcessorOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(5),
                MaxPollingInterval = TimeSpan.FromMilliseconds(10),
                TransportUnavailableBackoff = TimeSpan.FromMilliseconds(10),
            },
            logger).RunAsync);

        await sut.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (Volatile.Read(ref calls) == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await sut.StopAsync(default);

        var failure = logger.Records.ShouldHaveSingleItem();
        failure.EventId.ShouldBe(1400, "an activation failure has its own event");
        failure.Level.ShouldBe(LogLevel.Error);
        failure.Message.ShouldContain("its construction threw");
        failure.Message.ShouldNotContain("not registered");
        calls.ShouldBeGreaterThan(0, "the loop kept running and reached the processor once it activated");
    }

    // ---------------------------------------------------------------------------
    // Adaptive cadence — NextDelay is pure, so it is asserted directly
    // ---------------------------------------------------------------------------

    [Fact]
    public void NextDelay_WhenBatchSaturated_PollsAgainImmediately()
    {
        var sut = BuildWorker(Substitute.For<IOutboxProcessor>(), out _, CadenceOptions);
        var saturated = new OutboxBatchResult(
            Claimed: 10, Published: 10, Retried: 0, DeadLettered: 0, Released: 0,
            OutboxBatchAbortReason.None);

        sut.NextDelay(saturated, TimeSpan.FromSeconds(5))
            .ShouldBe(TimeSpan.Zero, "a full batch means more work is almost certainly waiting");
    }

    [Fact]
    public void NextDelay_WhenQueueEmpty_GrowsGeometricallyTowardTheIdleCeiling()
    {
        var sut = BuildWorker(Substitute.For<IOutboxProcessor>(), out _, CadenceOptions);

        sut.NextDelay(OutboxBatchResult.Empty, TimeSpan.FromSeconds(5)).ShouldBe(TimeSpan.FromSeconds(10));
        sut.NextDelay(OutboxBatchResult.Empty, TimeSpan.FromSeconds(10)).ShouldBe(TimeSpan.FromSeconds(20));
        // Clamped at MaxPollingInterval, never past it.
        sut.NextDelay(OutboxBatchResult.Empty, TimeSpan.FromSeconds(40)).ShouldBe(TimeSpan.FromMinutes(1));
        sut.NextDelay(OutboxBatchResult.Empty, TimeSpan.FromMinutes(1)).ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void NextDelay_WhenPartialBatch_ReturnsToTheBaseCadence()
    {
        var sut = BuildWorker(Substitute.For<IOutboxProcessor>(), out _, CadenceOptions);
        var partial = new OutboxBatchResult(
            Claimed: 3, Published: 3, Retried: 0, DeadLettered: 0, Released: 0,
            OutboxBatchAbortReason.None);

        sut.NextDelay(partial, TimeSpan.FromMinutes(1)).ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void NextDelay_WhenTransportUnavailable_BacksOffTowardTheOutageCeiling()
    {
        var sut = BuildWorker(Substitute.For<IOutboxProcessor>(), out _, CadenceOptions);
        var outage = new OutboxBatchResult(
            Claimed: 3, Published: 0, Retried: 0, DeadLettered: 0, Released: 3,
            OutboxBatchAbortReason.TransportUnavailable);

        // Grows past the idle ceiling: there is nothing to gain from probing a broker that is down.
        sut.NextDelay(outage, TimeSpan.FromMinutes(1)).ShouldBe(TimeSpan.FromMinutes(2));
        sut.NextDelay(outage, TimeSpan.FromMinutes(4)).ShouldBe(TimeSpan.FromMinutes(5));
        sut.NextDelay(outage, TimeSpan.FromMinutes(5)).ShouldBe(TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// A dispatcher that cannot be built backs off like an outage, even when the abandoned batch
    /// was full.
    /// </summary>
    /// <remarks>
    /// Saturated on purpose. An abandoned batch still reports every row it claimed, so a full one
    /// matches the saturation arm — poll again immediately — unless the abort reason is checked
    /// first. The rows it released are claimable at once, so that ordering would re-claim them,
    /// fail the same way and spin against the database.
    /// </remarks>
    [Fact]
    public void NextDelay_WhenTheDispatcherCannotBeActivated_BacksOffTowardTheOutageCeiling()
    {
        var sut = BuildWorker(Substitute.For<IOutboxProcessor>(), out _, CadenceOptions);
        var abandoned = new OutboxBatchResult(
            Claimed: 10, Published: 0, Retried: 0, DeadLettered: 0, Released: 10,
            OutboxBatchAbortReason.DispatcherActivationFailed);

        abandoned.IsSaturated(CadenceOptions.BatchSize).ShouldBeTrue("the case the ordering exists for");

        sut.NextDelay(abandoned, TimeSpan.FromMinutes(1)).ShouldBe(TimeSpan.FromMinutes(2));
        sut.NextDelay(abandoned, TimeSpan.FromMinutes(4)).ShouldBe(TimeSpan.FromMinutes(5));
        sut.NextDelay(abandoned, TimeSpan.FromMinutes(5)).ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void NextDelay_WhenCancelled_LeavesTheIntervalAlone()
    {
        var sut = BuildWorker(Substitute.For<IOutboxProcessor>(), out _, CadenceOptions);
        var cancelled = new OutboxBatchResult(
            Claimed: 2, Published: 1, Retried: 0, DeadLettered: 0, Released: 1,
            OutboxBatchAbortReason.Cancelled);

        // The loop is about to exit; the delay is irrelevant.
        sut.NextDelay(cancelled, TimeSpan.FromSeconds(7)).ShouldBe(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void NextDelay_WhenGrowingFromZero_RestartsAtTheBaseCadence()
    {
        // A saturated batch sets the delay to zero; the next idle pass must not stay at zero.
        var sut = BuildWorker(Substitute.For<IOutboxProcessor>(), out _, CadenceOptions);

        sut.NextDelay(OutboxBatchResult.Empty, TimeSpan.Zero).ShouldBe(TimeSpan.FromSeconds(5));
    }

    // ---------------------------------------------------------------------------
    // Misconfiguration surfaced by the processor stops the worker
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_WhenTheProcessorThrowsAConfigurationException_StopsTheLoop()
    {
        var callCount = 0;
        var coordinator = Substitute.For<IOutboxProcessor>();
        coordinator
            .ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                System.Threading.Interlocked.Increment(ref callCount);
                return ValueTask.FromException<OutboxBatchResult>(
                    new OutboxConfigurationException("IOutboxDispatcher is not registered."));
            });

        var sut = BuildWorker(coordinator, out _);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await sut.StopAsync(default);

        // Unlike a transient fault, this must NOT be retried: it cannot fix itself.
        callCount.ShouldBe(1);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static OutboxProcessorOptions CadenceOptions => new()
    {
        BatchSize = 10,
        PollingInterval = TimeSpan.FromSeconds(5),
        MaxPollingInterval = TimeSpan.FromMinutes(1),
        TransportUnavailableBackoff = TimeSpan.FromMinutes(5),
    };

    private static OutboxLoopHarness BuildWorker(
        IOutboxProcessor coordinator,
        out IServiceProvider provider,
        OutboxProcessorOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddKeyedScoped<IOutboxProcessor>(LoopHarness.Store, (_, _) => coordinator);
        services.AddLogging();
        provider = services.BuildServiceProvider();

        return new OutboxLoopHarness(new OutboxLoop(
            provider.GetRequiredService<IServiceScopeFactory>(),
            LoopHarness.Store,
            options ?? new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(10) },
            NullLogger<OutboxLoop>.Instance));
    }

}
