namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// The gate every batch of every partitioned store's instance passes (#191 D6, A6): waiters are admitted
/// in arrival order, a released permit goes to the oldest waiter rather than to a newcomer, and a release
/// racing a cancellation never loses a permit.
/// </summary>
public sealed class FifoGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Release_HandsThePermitToTheOldestWaiter_InArrivalOrder()
    {
        var gate = new FifoGate(1);
        await gate.EnterAsync();

        var waiters = Enumerable.Range(0, 5).Select(_ => gate.EnterAsync().AsTask()).ToArray();
        gate.Waiting.ShouldBe(5, "control: every waiter is queued behind the holder");

        for (var i = 0; i < waiters.Length; i++)
        {
            gate.Release();
            await waiters[i].WaitAsync(Timeout);
            for (var later = i + 1; later < waiters.Length; later++)
            {
                waiters[later].IsCompleted.ShouldBeFalse($"waiter {later} arrived after waiter {i}, so it waits for the next release");
            }
        }

        gate.Release();
        gate.Available.ShouldBe(1);
    }

    [Fact]
    public async Task AReleasedPermit_GoesToTheWaiter_NotToANewcomer()
    {
        var gate = new FifoGate(1);
        await gate.EnterAsync();
        var waiter = gate.EnterAsync().AsTask();

        gate.Release();
        var newcomer = gate.EnterAsync().AsTask();

        await waiter.WaitAsync(Timeout);
        newcomer.IsCompleted.ShouldBeFalse("the permit was handed to the waiter: a newcomer queues behind it");
        gate.Available.ShouldBe(0);

        gate.Release();
        await newcomer.WaitAsync(Timeout);
        gate.Release();
        gate.Available.ShouldBe(1);
    }

    [Fact]
    public async Task ACancelledWaiter_IsWithdrawn_AndNeverHandedAPermit()
    {
        var gate = new FifoGate(1);
        await gate.EnterAsync();
        using var cancel = new CancellationTokenSource();
        var cancelled = gate.EnterAsync(cancel.Token).AsTask();
        var next = gate.EnterAsync().AsTask();

        await cancel.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => cancelled.WaitAsync(Timeout));
        gate.Waiting.ShouldBe(1, "only the waiter behind it is still queued");

        gate.Release();
        await next.WaitAsync(Timeout);
        gate.Release();
        gate.Available.ShouldBe(1, "the cancelled waiter holds no permit");
    }

    [Fact]
    public async Task AWaiterHandedAPermit_KeepsIt_WhenItsTokenFiresAfterwards()
    {
        var gate = new FifoGate(1);
        await gate.EnterAsync();
        using var cancel = new CancellationTokenSource();
        var waiter = gate.EnterAsync(cancel.Token).AsTask();

        gate.Release();
        await cancel.CancelAsync();

        await waiter.WaitAsync(Timeout);
        gate.Available.ShouldBe(0, "the release took the waiter out of the queue: it owns the permit");
        gate.Release();
        gate.Available.ShouldBe(1);
    }

    [Fact]
    public async Task AReleaseRacingACancellation_NeverLosesAPermit()
    {
        const int Rounds = 10_000;
        var gate = new FifoGate(1);
        var granted = 0;
        var withdrawn = 0;

        for (var round = 0; round < Rounds; round++)
        {
            await gate.EnterAsync();
            using var cancel = new CancellationTokenSource();
            var waiter = gate.EnterAsync(cancel.Token).AsTask();

            using var start = new Barrier(2);
            var release = Task.Run(() =>
            {
                start.SignalAndWait();
                gate.Release();
            });
            var withdraw = Task.Run(() =>
            {
                start.SignalAndWait();
                cancel.Cancel();
            });
            await Task.WhenAll(release, withdraw).WaitAsync(Timeout);

            try
            {
                await waiter.WaitAsync(Timeout);
                granted++;
                gate.Release();
            }
            catch (OperationCanceledException)
            {
                withdrawn++;
            }

            gate.Available.ShouldBe(1, $"round {round}: every permit is back once the waiter is done with it");
            gate.Waiting.ShouldBe(0, $"round {round}: nobody is left queued");
        }

        (granted + withdrawn).ShouldBe(Rounds);
    }

    [Fact]
    public void Release_MoreTimesThanEntered_Throws()
    {
        var gate = new FifoGate(2);
        Should.Throw<InvalidOperationException>(gate.Release);
        gate.Available.ShouldBe(2);
    }
}
