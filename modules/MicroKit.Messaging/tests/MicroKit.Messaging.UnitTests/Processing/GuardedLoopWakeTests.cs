using MicroKit.Messaging.UnitTests.Bus;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// <see cref="GuardedLoop"/>'s wakeable wait (#215 D6, D6b), on a fake clock: a signal ends an armed
/// delay, no sooner than the floor after the claim's start — read on the clock the loop waits on — and
/// the losing wait leaves no timer behind; a removal or a host stop ends either wait.
/// </summary>
public sealed class GuardedLoopWakeTests
{
    private static readonly TimeSpan Floor = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task AWake_RightAfterAClaim_WaitsTheRestOfTheFloor_ThenStartsTheNextIteration()
    {
        var clock = new TestClock();
        using var host = new CancellationTokenSource();
        var loop = new ScriptedLoop(clock);
        var run = loop.RunAsync(host.Token);

        await Eventually.HoldsAsync(() => clock.CreatedTimers == 1, "the first iteration claimed and waits its idle delay");
        clock.Advance(TimeSpan.FromMilliseconds(300));
        loop.Wake.Signal();

        await Eventually.HoldsAsync(() => clock.CreatedTimers == 2, "the wake ended the idle wait and the floor's wait began");
        loop.Iterations.ShouldBe(1, "a wake 300 ms after the claim waits out the floor before the next claim");
        clock.ArmedTimers.ShouldBe(1, "the idle delay's timer was cancelled: only the floor's is left");

        clock.Advance(TimeSpan.FromMilliseconds(699));
        loop.Iterations.ShouldBe(1, "1 ms of the floor is left");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await Eventually.HoldsAsync(() => loop.Iterations == 2, "the floor ended: the next iteration runs");
        loop.StartedAt[1].ShouldBe(Floor, "the next claim starts one floor after the previous one, on the loop's clock");

        await host.CancelAsync();
        await run;
    }

    [Fact]
    public async Task AWake_AfterTheFloorHasPassed_StartsTheNextIterationAtOnce_AndLeavesNoTimerBehind()
    {
        var clock = new TestClock();
        using var host = new CancellationTokenSource();
        var loop = new ScriptedLoop(clock);
        var run = loop.RunAsync(host.Token);

        await Eventually.HoldsAsync(() => clock.CreatedTimers == 1, "the first iteration claimed and waits its idle delay");
        clock.Advance(Floor + TimeSpan.FromMilliseconds(500));
        loop.Wake.Signal();

        await Eventually.HoldsAsync(() => loop.Iterations == 2 && clock.CreatedTimers == 2, "the wake started the next iteration, which waits its own delay");
        loop.StartedAt[1].ShouldBe(Floor + TimeSpan.FromMilliseconds(500), "no clock time passed between the wake and the claim");
        clock.CreatedTimers.ShouldBe(2, "no floor was waited: the previous claim was older than the floor");
        clock.ArmedTimers.ShouldBe(1, "the cancelled idle delay left no timer behind: only the new iteration's is armed");

        await host.CancelAsync();
        await run;
    }

    [Fact]
    public async Task ASignalPendingWhenTheDelayBegins_EndsTheWaitAtTheFloor()
    {
        var clock = new TestClock();
        using var host = new CancellationTokenSource();
        var loop = new ScriptedLoop(clock) { DuringIteration = static (wake, iteration) => { if (iteration == 1) wake.Signal(); } };
        var run = loop.RunAsync(host.Token);

        // The signal came during the first iteration, after the drain: no race, the floor alone is
        // waited. Signalled once only, so a loop that ignored the floor fails here rather than spinning.
        await Eventually.HoldsAsync(() => clock.CreatedTimers == 1, "the first iteration waits the floor");
        loop.Iterations.ShouldBe(1);
        clock.Advance(Floor);
        await Eventually.HoldsAsync(() => loop.Iterations == 2, "the pending signal ended the wait at the floor");
        loop.StartedAt[1].ShouldBe(Floor);

        await host.CancelAsync();
        await run;
    }

    [Fact]
    public async Task ARemoval_DuringTheFloorsWait_EndsTheLoop()
    {
        var clock = new TestClock();
        using var host = new CancellationTokenSource();
        using var removal = new CancellationTokenSource();
        var loop = new ScriptedLoop(clock);
        var run = loop.RunAsync(host.Token, removal.Token);

        await Eventually.HoldsAsync(() => clock.CreatedTimers == 1, "the first iteration waits its idle delay");
        loop.Wake.Signal();
        await Eventually.HoldsAsync(() => clock.CreatedTimers == 2, "the floor's wait began");

        await removal.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        loop.Iterations.ShouldBe(1, "a removal ends the loop during the floor; it starts no iteration");
        clock.ArmedTimers.ShouldBe(0);
    }

    [Fact]
    public async Task AHostStop_DuringTheWakeableWait_EndsTheLoop_AndLeavesNoTimerBehind()
    {
        var clock = new TestClock();
        using var host = new CancellationTokenSource();
        var loop = new ScriptedLoop(clock);
        var run = loop.RunAsync(host.Token);

        await Eventually.HoldsAsync(() => clock.CreatedTimers == 1, "the first iteration waits its idle delay");
        await host.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        loop.Iterations.ShouldBe(1);
        clock.ArmedTimers.ShouldBe(0);
    }

    /// <summary>
    /// A loop whose every iteration claims, arms its wake with <see cref="Floor"/> and returns
    /// <see cref="IdleDelay"/>, as an outbox loop does after an empty batch.
    /// </summary>
    private sealed class ScriptedLoop(TestClock clock)
    {
        private readonly long _origin = clock.GetTimestamp();
        private int _iterations;

        public LoopWake Wake { get; } = new();

        /// <summary>Runs during each iteration, after the drain, with the iteration's 1-based number.</summary>
        public Action<LoopWake, int>? DuringIteration { get; init; }

        public int Iterations => Volatile.Read(ref _iterations);

        public System.Collections.Concurrent.ConcurrentDictionary<int, TimeSpan> StartedAt { get; } = new();

        public Task RunAsync(CancellationToken host, CancellationToken removal = default) => GuardedLoop.RunAsync(
            _ =>
            {
                Wake.ClaimStarting();
                StartedAt[Iterations] = clock.GetElapsedTime(_origin);
                DuringIteration?.Invoke(Wake, Iterations + 1);
                Wake.Arm(Floor);
                Interlocked.Increment(ref _iterations);
                return ValueTask.FromResult<TimeSpan?>(IdleDelay);
            },
            _ => IdleDelay,
            clock,
            Wake,
            host,
            removal);
    }
}
