using MicroKit.Data.Abstractions;
using Moq;

namespace MicroKit.Data.Tests;

public sealed class UnitOfWorkTests
{
    [Fact]
    public async Task SaveChangesAsync_ReturnsAffectedRowCount()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(3);

        var count = await uow.Object.SaveChangesAsync();

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task SaveChangesAsync_PassesCancellationToken()
    {
        var cts = new CancellationTokenSource();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(cts.Token)).ReturnsAsync(1);

        await uow.Object.SaveChangesAsync(cts.Token);

        uow.Verify(u => u.SaveChangesAsync(cts.Token), Times.Once);
    }
}
