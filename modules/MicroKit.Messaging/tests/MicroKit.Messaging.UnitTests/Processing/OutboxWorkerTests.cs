namespace MicroKit.Messaging.UnitTests.Processing;

public sealed class OutboxWorkerTests
{
    // ---------------------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_StartsLoop_CallsCoordinatorExecuteAsync()
    {
        var coordinator = Substitute.For<IOutboxCoordinator>();
        var sut = BuildWorker(coordinator, out _);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await sut.StartAsync(cts.Token);
        await Task.Delay(200);
        await sut.StopAsync(default);

        await coordinator.Received().ExecuteAsync(Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // Cancellation
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_StopsGracefully()
    {
        var coordinator = Substitute.For<IOutboxCoordinator>();
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
    public async Task ExecuteAsync_WhenCoordinatorThrowsTransientException_LogsErrorAndContinues()
    {
        var callCount = 0;
        var coordinator = Substitute.For<IOutboxCoordinator>();
        coordinator
            .ExecuteAsync(Arg.Any<CancellationToken>())
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

        // Worker must have continued after the exception (called coordinator more than once)
        callCount.ShouldBeGreaterThan(1);
    }

    // ---------------------------------------------------------------------------
    // Resilience — misconfiguration stops worker
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenCoordinatorResolutionFails_LogsCriticalAndStopsWorker()
    {
        // No IOutboxCoordinator registered → GetRequiredService throws InvalidOperationException
        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var sut = new OutboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(1) },
            NullLogger<OutboxWorker>.Instance);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100); // Let the first iteration run → worker returns

        var stopTask = sut.StopAsync(default);
        var winner = await Task.WhenAny(stopTask, Task.Delay(2000));

        winner.ShouldBe(stopTask, "StopAsync should complete quickly because worker already stopped");
    }

    // ---------------------------------------------------------------------------
    // Adaptive cadence — NextDelay is pure, so it is asserted directly
    // ---------------------------------------------------------------------------

    [Fact]
    public void NextDelay_WhenBatchSaturated_PollsAgainImmediately()
    {
        var sut = BuildWorker(Substitute.For<IOutboxCoordinator>(), out _, CadenceOptions);
        var saturated = new OutboxBatchResult(
            Claimed: 10, Published: 10, Retried: 0, DeadLettered: 0, Released: 0,
            OutboxBatchAbortReason.None);

        sut.NextDelay(saturated, TimeSpan.FromSeconds(5))
            .ShouldBe(TimeSpan.Zero, "a full batch means more work is almost certainly waiting");
    }

    [Fact]
    public void NextDelay_WhenQueueEmpty_GrowsGeometricallyTowardTheIdleCeiling()
    {
        var sut = BuildWorker(Substitute.For<IOutboxCoordinator>(), out _, CadenceOptions);

        sut.NextDelay(OutboxBatchResult.Empty, TimeSpan.FromSeconds(5)).ShouldBe(TimeSpan.FromSeconds(10));
        sut.NextDelay(OutboxBatchResult.Empty, TimeSpan.FromSeconds(10)).ShouldBe(TimeSpan.FromSeconds(20));
        // Clamped at MaxPollingInterval, never past it.
        sut.NextDelay(OutboxBatchResult.Empty, TimeSpan.FromSeconds(40)).ShouldBe(TimeSpan.FromMinutes(1));
        sut.NextDelay(OutboxBatchResult.Empty, TimeSpan.FromMinutes(1)).ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void NextDelay_WhenPartialBatch_ReturnsToTheBaseCadence()
    {
        var sut = BuildWorker(Substitute.For<IOutboxCoordinator>(), out _, CadenceOptions);
        var partial = new OutboxBatchResult(
            Claimed: 3, Published: 3, Retried: 0, DeadLettered: 0, Released: 0,
            OutboxBatchAbortReason.None);

        sut.NextDelay(partial, TimeSpan.FromMinutes(1)).ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void NextDelay_WhenTransportUnavailable_BacksOffTowardTheOutageCeiling()
    {
        var sut = BuildWorker(Substitute.For<IOutboxCoordinator>(), out _, CadenceOptions);
        var outage = new OutboxBatchResult(
            Claimed: 3, Published: 0, Retried: 0, DeadLettered: 0, Released: 3,
            OutboxBatchAbortReason.TransportUnavailable);

        // Grows past the idle ceiling: there is nothing to gain from probing a broker that is down.
        sut.NextDelay(outage, TimeSpan.FromMinutes(1)).ShouldBe(TimeSpan.FromMinutes(2));
        sut.NextDelay(outage, TimeSpan.FromMinutes(4)).ShouldBe(TimeSpan.FromMinutes(5));
        sut.NextDelay(outage, TimeSpan.FromMinutes(5)).ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void NextDelay_WhenCancelled_LeavesTheIntervalAlone()
    {
        var sut = BuildWorker(Substitute.For<IOutboxCoordinator>(), out _, CadenceOptions);
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
        var sut = BuildWorker(Substitute.For<IOutboxCoordinator>(), out _, CadenceOptions);

        sut.NextDelay(OutboxBatchResult.Empty, TimeSpan.Zero).ShouldBe(TimeSpan.FromSeconds(5));
    }

    // ---------------------------------------------------------------------------
    // Misconfiguration surfaced by the processor stops the worker
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenCoordinatorThrowsConfigurationException_StopsWorker()
    {
        var callCount = 0;
        var coordinator = Substitute.For<IOutboxCoordinator>();
        coordinator
            .ExecuteAsync(Arg.Any<CancellationToken>())
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

    private static OutboxWorker BuildWorker(
        IOutboxCoordinator coordinator,
        out IServiceProvider provider,
        OutboxProcessorOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IOutboxCoordinator>(_ => coordinator);
        services.AddLogging();
        provider = services.BuildServiceProvider();

        return new OutboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options ?? new OutboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(10) },
            NullLogger<OutboxWorker>.Instance);
    }

}
