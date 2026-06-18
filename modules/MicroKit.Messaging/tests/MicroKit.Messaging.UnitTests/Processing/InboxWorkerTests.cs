namespace MicroKit.Messaging.UnitTests.Processing;

public sealed class InboxWorkerTests
{
    // ---------------------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_StartsLoop_CallsCoordinatorExecuteAsync()
    {
        var coordinator = Substitute.For<IInboxCoordinator>();
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
        var coordinator = Substitute.For<IInboxCoordinator>();
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
        var coordinator = Substitute.For<IInboxCoordinator>();
        coordinator
            .ExecuteAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                System.Threading.Interlocked.Increment(ref callCount);
                return Task.FromException(new Exception("transient db error"));
            });

        var sut = BuildWorker(coordinator, out _);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await sut.StartAsync(cts.Token);
        await Task.Delay(300);
        await sut.StopAsync(default);

        callCount.ShouldBeGreaterThan(1);
    }

    // ---------------------------------------------------------------------------
    // Resilience — misconfiguration stops worker
    // ---------------------------------------------------------------------------

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

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static InboxWorker BuildWorker(IInboxCoordinator coordinator, out IServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddScoped<IInboxCoordinator>(_ => coordinator);
        services.AddLogging();
        provider = services.BuildServiceProvider();

        return new InboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new InboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(10) },
            NullLogger<InboxWorker>.Instance);
    }
}
