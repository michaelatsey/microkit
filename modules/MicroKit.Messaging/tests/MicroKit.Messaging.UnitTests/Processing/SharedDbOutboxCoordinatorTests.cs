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
}
