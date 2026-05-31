using MicroKit.Persistence.Testing;

namespace MicroKit.Persistence.UnitTests.Testing;

public sealed class InMemoryUnitOfWorkTests
{
    [Fact]
    public async Task CommitAsync_Increments_CommitCount()
    {
        var uow = new InMemoryUnitOfWork();

        await uow.CommitAsync();

        uow.CommitCount.ShouldBe(1);
    }

    [Fact]
    public async Task CommitAsync_CalledMultipleTimes_CountIsAccurate()
    {
        var uow = new InMemoryUnitOfWork();

        await uow.CommitAsync();
        await uow.CommitAsync();
        await uow.CommitAsync();

        uow.CommitCount.ShouldBe(3);
    }

    [Fact]
    public async Task CommitAsync_WhenCancelled_ThrowsOperationCancelled()
    {
        var uow = new InMemoryUnitOfWork();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await uow.CommitAsync(cts.Token));
    }

    [Fact]
    public async Task CommitAsync_WhenCancelled_DoesNotIncrementCommitCount()
    {
        var uow = new InMemoryUnitOfWork();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await uow.CommitAsync(cts.Token));

        uow.CommitCount.ShouldBe(0);
    }
}
