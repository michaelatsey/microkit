namespace MicroKit.Messaging.UnitTests.Processing;

public sealed class SharedDbOutboxCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_DelegatesToProcessor_WithConfiguredBatchSize()
    {
        var processor = Substitute.For<IOutboxProcessor>();
        var options = new OutboxProcessorOptions { BatchSize = 17 };

        var sut = new SharedDbOutboxCoordinator(processor, options);
        await sut.ExecuteAsync(CancellationToken.None);

        await processor.Received(1).ProcessBatchAsync(17, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PassesCancellationTokenToProcessor()
    {
        var processor = Substitute.For<IOutboxProcessor>();
        var options = new OutboxProcessorOptions { BatchSize = 10 };
        using var cts = new CancellationTokenSource();

        var sut = new SharedDbOutboxCoordinator(processor, options);
        await sut.ExecuteAsync(cts.Token);

        await processor.Received(1).ProcessBatchAsync(Arg.Any<int>(), cts.Token);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsTheBatchResult()
    {
        // The whole reason the signature changed: a coordinator that discarded this would
        // leave the worker on a fixed timer (ADR-MSG-015).
        var expected = new OutboxBatchResult(
            Claimed: 5, Published: 4, Retried: 1, DeadLettered: 0, Released: 0,
            OutboxBatchAbortReason.None);

        var processor = Substitute.For<IOutboxProcessor>();
        processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(expected));

        var sut = new SharedDbOutboxCoordinator(processor, new OutboxProcessorOptions());

        (await sut.ExecuteAsync(CancellationToken.None)).ShouldBe(expected);
    }
}
