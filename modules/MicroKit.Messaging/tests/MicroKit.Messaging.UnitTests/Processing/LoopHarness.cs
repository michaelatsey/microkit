namespace MicroKit.Messaging.UnitTests.Processing;

using Microsoft.Extensions.Hosting;

/// <summary>Runs one store loop as a hosted service, so a test can start and stop it.</summary>
internal class LoopHarness(Func<CancellationToken, Task> run) : BackgroundService
{
    /// <summary>The store every loop test drains.</summary>
    public static readonly MessageStoreKey Store = new("tests:loop-store");

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => run(stoppingToken);
}

/// <summary>An <see cref="OutboxLoop"/> run as a hosted service, its cadence exposed.</summary>
internal sealed class OutboxLoopHarness(OutboxLoop loop) : LoopHarness(loop.RunAsync)
{
    public TimeSpan NextDelay(OutboxBatchResult result, TimeSpan current) => loop.NextDelay(result, current);
}

/// <summary>An <see cref="InboxLoop"/> run as a hosted service, its cadence exposed.</summary>
internal sealed class InboxLoopHarness(InboxLoop loop) : LoopHarness(loop.RunAsync)
{
    public TimeSpan NextDelay(InboxBatchResult result, TimeSpan current) => loop.NextDelay(result, current);
}
