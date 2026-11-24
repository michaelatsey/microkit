namespace MicroKit.Messaging.UnitTests.Processing;

using MicroKit.Messaging.UnitTests.Bus;

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

    /// <summary>
    /// Waits for the call, never a fixed window (#213): CI run 45364049251 failed the fixed-window
    /// version. The start token is <see cref="CancellationToken.None"/> because the token handed to
    /// <c>BackgroundService.StartAsync</c> is linked to the loop's stopping token, so a timed one
    /// ends the loop.
    /// </summary>
    [Fact]
    public async Task RunAsync_CallsTheStoresProcessor()
    {
        var calls = 0;
        var coordinator = Substitute.For<IOutboxProcessor>();
        coordinator
            .When(c => c.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()))
            .Do(_ => Interlocked.Increment(ref calls));
        var sut = BuildWorker(coordinator, out _);

        await sut.StartAsync(CancellationToken.None);
        await UntilAsync(() => Volatile.Read(ref calls) >= 1);
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

        // A counted condition, and no timed start token: see RunAsync_CallsTheStoresProcessor (#213).
        await sut.StartAsync(CancellationToken.None);
        await UntilAsync(() => Volatile.Read(ref callCount) > 1);
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

    /// <summary>
    /// A down endpoint is not the loop's to back off (#188): its rows were deferred to the end of
    /// the endpoint's own back-off, so the loop keeps the cadence of the rows behind them.
    /// </summary>
    [Fact]
    public void NextDelay_WhenAnEndpointWasDown_KeepsThePartialCadence()
    {
        var sut = BuildWorker(Substitute.For<IOutboxProcessor>(), out _, CadenceOptions);
        var outage = new OutboxBatchResult(
            Claimed: 3, Published: 0, Retried: 0, DeadLettered: 0, Released: 3,
            OutboxBatchAbortReason.TransportUnavailable);

        sut.NextDelay(outage, TimeSpan.FromMinutes(1)).ShouldBe(CadenceOptions.PollingInterval);
    }

    /// <summary>
    /// A full batch whose endpoint was down polls again at once: its rows are deferred, so the next
    /// claim reaches the rows behind them instead of re-claiming the same ones.
    /// </summary>
    [Fact]
    public void NextDelay_WhenAFullBatchsEndpointWasDown_PollsAgainAtOnce()
    {
        var sut = BuildWorker(Substitute.For<IOutboxProcessor>(), out _, CadenceOptions);
        var deferred = new OutboxBatchResult(
            Claimed: 10, Published: 0, Retried: 0, DeadLettered: 0, Released: 10,
            OutboxBatchAbortReason.DispatcherActivationFailed);

        deferred.IsSaturated(CadenceOptions.BatchSize).ShouldBeTrue();
        sut.NextDelay(deferred, TimeSpan.FromMinutes(1)).ShouldBe(TimeSpan.Zero);
    }

    /// <summary>
    /// A full batch of rows handed over while their endpoint is in back-off — a store that does not
    /// honour the claim's endpoint filter, against its contract — waits the polling interval rather
    /// than rewriting the down endpoint's backlog back to back.
    /// </summary>
    [Fact]
    public void NextDelay_WhenAFullBatchCarriedRowsHandedOverInBackOff_WaitsThePollingInterval()
    {
        var sut = BuildWorker(Substitute.For<IOutboxProcessor>(), out _, CadenceOptions);
        var handedOver = new OutboxBatchResult(
            Claimed: 10, Published: 0, Retried: 0, DeadLettered: 0, Released: 10,
            OutboxBatchAbortReason.EndpointInBackOff) { HandedOverInBackOff = true };
        // The first verdict was the other endpoint's: the flag, not the reason, decides.
        var mixed = handedOver with { Published = 4, Released = 6, AbortReason = OutboxBatchAbortReason.TransportUnavailable };

        handedOver.IsSaturated(CadenceOptions.BatchSize).ShouldBeTrue();
        sut.NextDelay(handedOver, TimeSpan.Zero).ShouldBe(CadenceOptions.PollingInterval);
        sut.NextDelay(mixed, TimeSpan.Zero).ShouldBe(CadenceOptions.PollingInterval,
            "one delivered row must not send a store that breaks the filter contract round the down endpoint's backlog at once");
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
    // Misconfiguration surfaced by the processor: reported and backed off, never the end of the loop
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_WhenTheProcessorThrowsAConfigurationException_BacksOffAndKeepsRunning()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var calls = new System.Collections.Concurrent.ConcurrentQueue<TimeSpan>();
        var coordinator = Substitute.For<IOutboxProcessor>();
        coordinator
            .ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Enqueue(clock.Elapsed);
                return ValueTask.FromException<OutboxBatchResult>(new OutboxConfigurationException("IOutboxDispatcher is not registered."));
            });

        var sut = BuildWorker(coordinator, out _);

        await sut.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => calls.Count >= 4, "only a host stop ends a loop: it keeps retrying");
        await sut.StopAsync(default);

        // PollingInterval is 10 ms: the delay doubles on each fault (20, 40, 80 ms…), never spun on.
        var at = calls.ToArray();
        (at[3] - at[2]).ShouldBeGreaterThan(TimeSpan.FromMilliseconds(50), "the back-off grows toward MaxPollingInterval");
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

    /// <summary>
    /// Returns once <paramref name="condition"/> holds, or after 10 s on a monotonic clock, so the
    /// caller's own assertion reports a miss.
    /// </summary>
    private static async Task UntilAsync(Func<bool> condition)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(10);
        }
    }

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
