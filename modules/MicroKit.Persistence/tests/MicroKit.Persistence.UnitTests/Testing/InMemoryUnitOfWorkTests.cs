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

    [Fact]
    public void DiscardChanges_Increments_DiscardCount()
    {
        var uow = new InMemoryUnitOfWork();

        uow.DiscardChanges();

        uow.DiscardCount.ShouldBe(1);
    }

    [Fact]
    public void DiscardChanges_CalledTwice_CountIsAccurate()
    {
        var uow = new InMemoryUnitOfWork();

        uow.DiscardChanges();
        uow.DiscardChanges();

        uow.DiscardCount.ShouldBe(2);
    }

    [Fact]
    public void DiscardChanges_OnFreshUnitOfWork_DoesNotThrow()
    {
        var uow = new InMemoryUnitOfWork();

        Should.NotThrow(() => uow.DiscardChanges());
    }

    [Fact]
    public void DiscardChanges_DoesNotAffectCommitCount()
    {
        var uow = new InMemoryUnitOfWork();

        uow.DiscardChanges();

        uow.CommitCount.ShouldBe(0, "a discard is the exit that does not commit");
    }

    [Fact]
    public async Task CommitAsync_DoesNotAffectDiscardCount()
    {
        var uow = new InMemoryUnitOfWork();

        await uow.CommitAsync();

        uow.DiscardCount.ShouldBe(0);
    }

    [Fact]
    public async Task CommitAsync_ThenDiscardChanges_CountsBothExitsIndependently()
    {
        var uow = new InMemoryUnitOfWork();

        await uow.CommitAsync();
        uow.DiscardChanges();

        uow.CommitCount.ShouldBe(1);
        uow.DiscardCount.ShouldBe(1);
    }
}
