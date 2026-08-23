namespace MicroKit.Messaging.UnitTests.Processing;

public sealed class InboxWorkerTests
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

    [Fact]
    public async Task ExecuteAsync_StartsLoop_CallsCoordinatorExecuteAsync()
    {
        var coordinator = Substitute.For<IInboxCoordinator>();
        coordinator.ExecuteAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(InboxBatchResult.Empty));

        var sut = BuildWorker(coordinator, out _);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await sut.StartAsync(cts.Token);
        await Task.Delay(200);
        await sut.StopAsync(default);

        await coordinator.Received().ExecuteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_StopsGracefully()
    {
        var coordinator = Substitute.For<IInboxCoordinator>();
        coordinator.ExecuteAsync(Arg.Any<CancellationToken>())
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
    public async Task ExecuteAsync_WhenCoordinatorThrowsTransientException_LogsErrorAndContinues()
    {
        var callCount = 0;
        var coordinator = Substitute.For<IInboxCoordinator>();
        coordinator
            .ExecuteAsync(Arg.Any<CancellationToken>())
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

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await sut.StartAsync(cts.Token);
        await Task.Delay(350);
        await sut.StopAsync(default);

        callCount.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCoordinatorResolutionFails_LogsCriticalAndStopsWorker()
    {
        var services = new ServiceCollection();
        services.AddLogging(); // no IInboxCoordinator
        var provider = services.BuildServiceProvider();

        var sut = new InboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new InboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(1) },
            NullLogger<InboxWorker>.Instance);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        var stopTask = sut.StopAsync(default);
        var winner = await Task.WhenAny(stopTask, Task.Delay(2000));

        winner.ShouldBe(stopTask, "StopAsync should complete quickly because worker already stopped");
    }

    /// <summary>
    /// A missing registration cannot fix itself without a redeployment. The processor has already
    /// settled and released the batch by then, so nothing is stranded — the worker stops rather
    /// than retrying forever and hiding the defect.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenProcessorReportsMisconfiguration_StopsWorker()
    {
        var callCount = 0;
        var coordinator = Substitute.For<IInboxCoordinator>();
        coordinator
            .ExecuteAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                return ValueTask.FromException<InboxBatchResult>(
                    new InboxConfigurationException("IInboxSettlementStore is not registered"));
            });

        var sut = BuildWorker(
            coordinator,
            out _,
            new InboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(5) });

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(default);

        callCount.ShouldBe(1, "the worker must stop after the first misconfiguration, not back off");
    }

    // -----------------------------------------------------------------------------------
    // Adaptive cadence — asserted against the pure function, not through a timing loop
    // -----------------------------------------------------------------------------------

    [Fact]
    public void NextDelay_WhenBatchSaturated_PollsAgainImmediately()
    {
        var sut = BuildWorker(Substitute.For<IInboxCoordinator>(), out _, CadenceOptions);
        var saturated = new InboxBatchResult(
            Claimed: 10, Processed: 10, Retried: 0, DeadLettered: 0, Released: 0,
            LeasesLost: 0, InboxBatchAbortReason.None);

        sut.NextDelay(saturated, TimeSpan.FromSeconds(5))
            .ShouldBe(TimeSpan.Zero, "a full batch means more work is almost certainly waiting");
    }

    [Fact]
    public void NextDelay_WhenQueueIdle_GrowsGeometricallyTowardTheCeiling()
    {
        var sut = BuildWorker(Substitute.For<IInboxCoordinator>(), out _, CadenceOptions);

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
        var sut = BuildWorker(Substitute.For<IInboxCoordinator>(), out _, CadenceOptions);
        var partial = new InboxBatchResult(
            Claimed: 3, Processed: 3, Retried: 0, DeadLettered: 0, Released: 0,
            LeasesLost: 0, InboxBatchAbortReason.None);

        sut.NextDelay(partial, TimeSpan.FromMinutes(1)).ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void NextDelay_WhenDependencyUnavailable_BacksOffHarderThanIdle()
    {
        var sut = BuildWorker(Substitute.For<IInboxCoordinator>(), out _, CadenceOptions);
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
        var sut = BuildWorker(Substitute.For<IInboxCoordinator>(), out _, CadenceOptions);
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
        var sut = BuildWorker(Substitute.For<IInboxCoordinator>(), out _, CadenceOptions);

        sut.NextDelay(InboxBatchResult.Empty, TimeSpan.Zero).ShouldBe(TimeSpan.FromSeconds(5));
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private static InboxWorker BuildWorker(
        IInboxCoordinator coordinator,
        out IServiceProvider provider,
        InboxProcessorOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => coordinator);
        services.AddLogging();
        provider = services.BuildServiceProvider();

        return new InboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options ?? new InboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(10) },
            NullLogger<InboxWorker>.Instance);
    }
}
