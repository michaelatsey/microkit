namespace MicroKit.Messaging.UnitTests.Processing;

public sealed class SharedDbInboxCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_DelegatesToProcessor_WithConfiguredBatchSize()
    {
        var processor = Substitute.For<IInboxProcessor>();
        processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(InboxBatchResult.Empty));
        var options = new InboxProcessorOptions { BatchSize = 13 };

        var sut = new SharedDbInboxCoordinator(processor, options);
        await sut.ExecuteAsync(CancellationToken.None);

        await processor.Received(1).ProcessBatchAsync(13, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PassesCancellationTokenToProcessor()
    {
        var processor = Substitute.For<IInboxProcessor>();
        processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(InboxBatchResult.Empty));
        var options = new InboxProcessorOptions { BatchSize = 10 };
        using var cts = new CancellationTokenSource();

        var sut = new SharedDbInboxCoordinator(processor, options);
        await sut.ExecuteAsync(cts.Token);

        await processor.Received(1).ProcessBatchAsync(Arg.Any<int>(), cts.Token);
    }

    /// <summary>
    /// The worker sets its next interval from this value, so a coordinator that swallowed it
    /// would silently restore the fixed-timer behaviour the result type exists to replace.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ReturnsTheProcessorsResultUnchanged()
    {
        var expected = new InboxBatchResult(
            Claimed: 9, Processed: 7, Retried: 1, DeadLettered: 1, Released: 0,
            LeasesLost: 2, InboxBatchAbortReason.None);

        var processor = Substitute.For<IInboxProcessor>();
        processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(expected));

        var sut = new SharedDbInboxCoordinator(processor, new InboxProcessorOptions());
        var result = await sut.ExecuteAsync(CancellationToken.None);

        result.ShouldBe(expected);
    }
}
