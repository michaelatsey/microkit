namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// An instance's loop (#191 D3, D5, D6): it prepares its scope for its instance, holds the gate from
/// claim to settlement and returns it on every path, and a removal ends it after the batch in flight
/// settles — never by cancelling it.
/// </summary>
public sealed class InstanceLoopTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ARemovedInstance_SettlesItsBatchInFlight_ThenStopsClaiming()
    {
        var inFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        CancellationToken batchToken = default;
        var processor = Substitute.For<IOutboxProcessor>();
        processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Batch(call.ArgAt<CancellationToken>(1)));

        async ValueTask<OutboxBatchResult> Batch(CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            batchToken = token;
            inFlight.TrySetResult();
            await settle.Task;
            return OutboxBatchResult.Empty;
        }

        using var harness = new InstanceHarness(configure: s => s.AddKeyedScoped(InstanceHarness.Store, (_, _) => processor));
        var instance = harness.Start("a");
        var loop = new OutboxLoop(
            harness.ScopeFactory, InstanceHarness.Store, harness.OutboxOptions, NullLogger<OutboxLoop>.Instance, instance);

        using var host = new CancellationTokenSource();
        var run = loop.RunAsync(host.Token);
        await inFlight.Task.WaitAsync(Timeout);
        harness.Gate.Available.ShouldBe(harness.Gate.Permits - 1, "control: the batch holds a permit");

        instance.Remove();
        batchToken.IsCancellationRequested.ShouldBeFalse("a removal never cancels the batch in flight: it settles");
        settle.SetResult();

        await run.WaitAsync(Timeout);
        calls.ShouldBe(1, "after the batch in flight, the removed instance claims nothing more");
        harness.Gate.Available.ShouldBe(harness.Gate.Permits, "its permit is back");
        harness.Initialized.ShouldAllBe(id => id == "a", "its scope was prepared for its instance");
        harness.Initialized.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ABatchThatThrows_ReturnsItsPermit_AndTheLoopGoesOn()
    {
        var calls = 0;
        var processor = Substitute.For<IOutboxProcessor>();
        processor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromException<OutboxBatchResult>(new InvalidOperationException("The claim failed."));
        });

        using var harness = new InstanceHarness(permits: 1, configure: s => s.AddKeyedScoped(InstanceHarness.Store, (_, _) => processor));
        var instance = harness.Start("a");
        var loop = new OutboxLoop(
            harness.ScopeFactory, InstanceHarness.Store, harness.OutboxOptions, NullLogger<OutboxLoop>.Instance, instance);

        using var host = new CancellationTokenSource();
        var run = loop.RunAsync(host.Token);

        // With one permit, a permit kept by the first failed batch would hold every later one.
        var deadline = DateTime.UtcNow + Timeout;
        while (Volatile.Read(ref calls) < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        calls.ShouldBeGreaterThanOrEqualTo(3, "each failed batch returned its permit, so the next one passed the gate");
        await host.CancelAsync();
        await run.WaitAsync(Timeout);
        harness.Gate.Available.ShouldBe(1);
    }

    [Fact]
    public async Task ALoopWaitingAtTheGate_WhenItsInstanceIsRemoved_EndsWithoutClaiming()
    {
        var claims = 0;
        var inboxProcessor = Substitute.For<IInboxProcessor>();
        inboxProcessor.ProcessBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref claims);
            return ValueTask.FromResult(InboxBatchResult.Empty);
        });
        using var harness = new InstanceHarness(permits: 1, configure: s => s.AddKeyedScoped(InstanceHarness.Store, (_, _) => inboxProcessor));
        await harness.Gate.EnterAsync();
        var instance = harness.Start("a");
        var loop = new InboxLoop(
            harness.ScopeFactory,
            InstanceHarness.Store,
            new InboxProcessorOptions { PollingInterval = TimeSpan.FromMilliseconds(5), MaxPollingInterval = TimeSpan.FromMilliseconds(20) },
            NullLogger<InboxLoop>.Instance,
            instance);

        using var host = new CancellationTokenSource();
        var run = loop.RunAsync(host.Token);
        var deadline = DateTime.UtcNow + Timeout;
        while (harness.Gate.Waiting == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        harness.Gate.Waiting.ShouldBe(1, "control: the loop waits at the gate");
        instance.Remove();
        await run.WaitAsync(Timeout);

        harness.Gate.Waiting.ShouldBe(0, "its wait was withdrawn");
        harness.Gate.Release();
        harness.Gate.Available.ShouldBe(1, "and it never took a permit");
        claims.ShouldBe(0, "it claimed nothing");
    }
}
