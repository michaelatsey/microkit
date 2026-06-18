namespace MicroKit.Messaging.UnitTests.Processing;

public sealed class SharedDbInboxCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_DelegatesToProcessor_WithConfiguredBatchSize()
    {
        var processor = Substitute.For<IInboxProcessor>();
        var options = new InboxProcessorOptions { BatchSize = 13 };

        var sut = new SharedDbInboxCoordinator(processor, options);
        await sut.ExecuteAsync(CancellationToken.None);

        await processor.Received(1).ProcessBatchAsync(13, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PassesCancellationTokenToProcessor()
    {
        var processor = Substitute.For<IInboxProcessor>();
        var options = new InboxProcessorOptions { BatchSize = 10 };
        using var cts = new CancellationTokenSource();

        var sut = new SharedDbInboxCoordinator(processor, options);
        await sut.ExecuteAsync(cts.Token);

        await processor.Received(1).ProcessBatchAsync(Arg.Any<int>(), cts.Token);
    }
}
