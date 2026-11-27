namespace MicroKit.Messaging.UnitTests.Processing;

using MicroKit.Messaging.UnitTests.Bus;

public sealed class InboxLoopTests
{
    private static InboxProcessorOptions CadenceOptions => new()
    {
        BatchSize = 10,
        PollingInterval = TimeSpan.FromSeconds(5),
        MaxPollingInterval = TimeSpan.FromMinutes(1),
        DependencyUnavailableBackoff = TimeSpan.FromMinutes(5),
    };

    // -----------------------------------------------------------------------------------
    // Loop
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The loop hands the processor the configured batch size and the host's own token — what the
    /// coordinator it replaced did (#188).
    /// </summary>
    [Fact]
    public async Task RunAsync_PassesTheConfiguredBatchSizeAndTheHostsToken()
    {
        var seen = new System.Collections.Concurrent.ConcurrentQueue<(int Size, CancellationToken Token)>();
        var processor = Substitute.For<IInboxProcessor>();
        processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                seen.Enqueue((ci.ArgAt<int>(0), ci.ArgAt<CancellationToken>(1)));
                return ValueTask.FromResult(InboxBatchResult.Empty);
            });

        var services = new ServiceCollection();
        services.AddKeyedScoped<IInboxProcessor>(LoopHarness.Store, (_, _) => processor);
        var provider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource();

        var run = new InboxLoop(
            provider.GetRequiredService<IServiceScopeFactory>(),
            LoopHarness.Store,
            new InboxProcessorOptions { BatchSize = 37, PollingInterval = TimeSpan.FromMilliseconds(5) },
            NullLogger<InboxLoop>.Instance).RunAsync(cts.Token);

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
        var processor = Substitute.For<IInboxProcessor>();
        processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref calls) == 1
                ? ValueTask.FromException<InboxBatchResult>(new ObjectDisposedException(nameof(IServiceProvider)))
                : ValueTask.FromResult(InboxBatchResult.Empty));

        var sut = BuildWorker(processor, out _, new InboxProcessorOptions
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
    /// Waits for the call, never a fixed window (#213, as #217 did for the outbox): CI run 46054316604 failed
    /// the fixed-window version of the transient-exception test below. The start token is
    /// <see cref="CancellationToken.None"/> because the token handed to <c>BackgroundService.StartAsync</c> is
    /// linked to the loop's stopping token, so a timed one ends the loop.
    /// </summary>
    [Fact]
    public async Task RunAsync_CallsTheStoresProcessor()
    {
        var calls = 0;
        var coordinator = Substitute.For<IInboxProcessor>();
        coordinator.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(InboxBatchResult.Empty);
            });

        var sut = BuildWorker(coordinator, out _);

        await sut.StartAsync(CancellationToken.None);
        await UntilAsync(() => Volatile.Read(ref calls) >= 1);
        await sut.StopAsync(default);

        await coordinator.Received().ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_StopsGracefully()
    {
        var coordinator = Substitute.For<IInboxProcessor>();
        coordinator.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(InboxBatchResult.Empty));

        var sut = BuildWorker(coordinator, out _);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.NotThrowAsync(async () =>
        {
            await sut.StartAsync(cts.Token);
            await Task.Delay(200);
            await sut.StopAsync(default);
        });
    }

    [Fact]
    public async Task RunAsync_WhenTheProcessorThrowsAConfigurationException_BacksOffAndKeepsRunning()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var calls = new System.Collections.Concurrent.ConcurrentQueue<TimeSpan>();
        var coordinator = Substitute.For<IInboxProcessor>();
        coordinator
            .ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Enqueue(clock.Elapsed);
                return ValueTask.FromException<InboxBatchResult>(new InboxConfigurationException("No IInboxGuardStore is registered for the store."));
            });

        var sut = BuildWorker(coordinator, out _);

        await sut.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => calls.Count >= 4, "only a host stop ends a loop: it keeps retrying");
        await sut.StopAsync(default);

        // PollingInterval is 10 ms: the delay doubles on each fault (20, 40, 80 ms…), never spun on.
        var at = calls.ToArray();
        (at[3] - at[2]).ShouldBeGreaterThan(TimeSpan.FromMilliseconds(50), "the back-off grows toward MaxPollingInterval");
    }

    [Fact]
    public async Task RunAsync_WhenTheProcessorThrowsATransientException_LogsAndContinues()
    {
        var callCount = 0;
        var coordinator = Substitute.For<IInboxProcessor>();
        coordinator
            .ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                return ValueTask.FromException<InboxBatchResult>(new Exception("transient db error"));
            });

        var sut = BuildWorker(
            coordinator,
            out _,
            new InboxProcessorOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(10),
                MaxPollingInterval = TimeSpan.FromMilliseconds(40),
            });

        // A counted condition, and no timed start token: see RunAsync_CallsTheStoresProcessor (#213).
        await sut.StartAsync(CancellationToken.None);
        await UntilAsync(() => Volatile.Read(ref callCount) > 1);
        await sut.StopAsync(default);

        callCount.ShouldBeGreaterThan(1);
    }

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
        var processor = Substitute.For<IInboxProcessor>();
        processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(InboxBatchResult.Empty);
            });

        var services = new ServiceCollection();
        services.AddKeyedScoped<IInboxProcessor>(LoopHarness.Store, (_, _) =>
            Interlocked.Increment(ref attempts) == 1
                ? throw new InvalidOperationException("The store's constructor threw.")
                : processor);
        var provider = services.BuildServiceProvider();
        var logger = new CapturingLogger<InboxLoop>();

        var sut = new LoopHarness(new InboxLoop(
            provider.GetRequiredService<IServiceScopeFactory>(),
            LoopHarness.Store,
            new InboxProcessorOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(5),
                MaxPollingInterval = TimeSpan.FromMilliseconds(10),
                DependencyUnavailableBackoff = TimeSpan.FromMilliseconds(10),
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

    // -----------------------------------------------------------------------------------
    // Adaptive cadence — asserted against the pure function, not through a timing loop
    // -----------------------------------------------------------------------------------

    [Fact]
    public void NextDelay_WhenBatchSaturated_PollsAgainImmediately()
    {
        var sut = BuildWorker(Substitute.For<IInboxProcessor>(), out _, CadenceOptions);
        var saturated = new InboxBatchResult(
            Claimed: 10, Processed: 10, Retried: 0, DeadLettered: 0, Released: 0,
            LeasesLost: 0, InboxBatchAbortReason.None);

        sut.NextDelay(saturated, TimeSpan.FromSeconds(5))
            .ShouldBe(TimeSpan.Zero, "a full batch means more work is almost certainly waiting");
    }

    [Fact]
    public void NextDelay_WhenQueueIdle_GrowsGeometricallyTowardTheCeiling()
    {
        var sut = BuildWorker(Substitute.For<IInboxProcessor>(), out _, CadenceOptions);

        sut.NextDelay(InboxBatchResult.Empty, TimeSpan.FromSeconds(5))
            .ShouldBe(TimeSpan.FromSeconds(10));
        sut.NextDelay(InboxBatchResult.Empty, TimeSpan.FromSeconds(10))
            .ShouldBe(TimeSpan.FromSeconds(20));
        sut.NextDelay(InboxBatchResult.Empty, TimeSpan.FromSeconds(40))
            .ShouldBe(TimeSpan.FromMinutes(1), "capped at MaxPollingInterval");
    }

    [Fact]
    public void NextDelay_WhenPartialBatch_ReturnsToBaseCadence()
    {
        var sut = BuildWorker(Substitute.For<IInboxProcessor>(), out _, CadenceOptions);
        var partial = new InboxBatchResult(
            Claimed: 3, Processed: 3, Retried: 0, DeadLettered: 0, Released: 0,
            LeasesLost: 0, InboxBatchAbortReason.None);

        sut.NextDelay(partial, TimeSpan.FromMinutes(1)).ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void NextDelay_WhenDependencyUnavailable_BacksOffHarderThanIdle()
    {
        var sut = BuildWorker(Substitute.For<IInboxProcessor>(), out _, CadenceOptions);
        var outage = new InboxBatchResult(
            Claimed: 10, Processed: 0, Retried: 0, DeadLettered: 0, Released: 10,
            LeasesLost: 0, InboxBatchAbortReason.DependencyUnavailable);

        // Grows toward DependencyUnavailableBackoff (5 min), past MaxPollingInterval (1 min):
        // there is nothing to gain from probing something already down.
        sut.NextDelay(outage, TimeSpan.FromMinutes(1)).ShouldBe(TimeSpan.FromMinutes(2));
        sut.NextDelay(outage, TimeSpan.FromMinutes(4)).ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void NextDelay_WhenCancelled_LeavesTheIntervalAlone()
    {
        var sut = BuildWorker(Substitute.For<IInboxProcessor>(), out _, CadenceOptions);
        var cancelled = new InboxBatchResult(
            Claimed: 4, Processed: 0, Retried: 0, DeadLettered: 0, Released: 4,
            LeasesLost: 0, InboxBatchAbortReason.Cancelled);

        // The loop is about to exit; the delay is irrelevant.
        sut.NextDelay(cancelled, TimeSpan.FromSeconds(30)).ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void NextDelay_GrowingFromZero_RestartsAtTheBaseInterval()
    {
        // A saturated batch sets the interval to zero; the next idle pass must not stay there.
        var sut = BuildWorker(Substitute.For<IInboxProcessor>(), out _, CadenceOptions);

        sut.NextDelay(InboxBatchResult.Empty, TimeSpan.Zero).ShouldBe(TimeSpan.FromSeconds(5));
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

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

    private static InboxLoopHarness BuildWorker(
        IInboxProcessor coordinator,
        out IServiceProvider provider,
        InboxProcessorOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddKeyedScoped<IInboxProcessor>(LoopHarness.Store, (_, _) => coordinator);
        services.AddLogging();
        provider = services.BuildServiceProvider();

        return new InboxLoopHarness(new InboxLoop(
            provider.GetRequiredService<IServiceScopeFactory>(),
            LoopHarness.Store,
            options ?? new InboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(10) },
            NullLogger<InboxLoop>.Instance));
    }
}
