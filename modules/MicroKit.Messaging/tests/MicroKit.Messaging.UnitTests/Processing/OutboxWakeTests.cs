using System.Collections.Concurrent;
using System.Diagnostics;

using MicroKit.Messaging.UnitTests.Bus;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// The outbox loop's wake (#215), on the real clock the loop runs on (#213): a signal during a batch
/// starts the next one at the floor (D5); only an idle or a partial batch's delay is cut, never a
/// failure's, an activation's, a configuration fault's, a preparation's or the hand-over guard's (D6);
/// any number of signals during one wait cost one batch.
/// </summary>
/// <remarks>
/// Every timing assertion waits for the next claim and asserts when it started. A wait — a delay or the
/// floor — is asserted to <see cref="Tolerance"/> of itself: a wake that should not have happened would
/// come at the floor, half the delay or less, and fail.
/// </remarks>
public sealed class OutboxWakeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly MessageStoreKey Store = LoopHarness.Store;

    /// <summary>
    /// How far a real-time wait may end short of itself against the Stopwatch: max(10 ms, 5% of the wait).
    /// Measured shortfall up to 4.2 ms at 100 ms and 5.9 ms at 500 ms over 40 runs (2026-11-23, WSL2),
    /// so drift grows with the wait; max(10 ms, 5%) covers it 2.4x at 100 ms and 4.2x at 500 ms.
    /// </summary>
    private static TimeSpan Tolerance(TimeSpan wait)
    {
        var proportional = wait * 0.05;
        var least = TimeSpan.FromMilliseconds(10);
        return proportional > least ? proportional : least;
    }

    // ---------------------------------------------------------------------------
    // D5 — drained before the claim, never after the batch
    // ---------------------------------------------------------------------------

    /// <summary>V4: a commit while the processor holds a batch starts the next batch at the floor.</summary>
    [Fact]
    public async Task ASignalDuringABatch_StartsTheNextBatchAtTheFloor_NotAfterTheIdleDelay()
    {
        var options = Options(polling: TimeSpan.FromMilliseconds(100));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wake = new LoopWake();
        var processor = new RecordingOutboxProcessor(wake, async (n, _) =>
        {
            if (n == 3)
            {
                await release.Task;
            }

            return OutboxBatchResult.Empty;
        });
        await using var loop = Run(processor, options, wake);

        // Two empty batches grew the delay to 400 ms; the third, held, returns empty too: 800 ms.
        await Eventually.HoldsAsync(() => processor.Started == 3, "the third batch is in flight");
        wake.Signal();
        var released = Stopwatch.GetTimestamp();
        release.SetResult();

        await Eventually.HoldsAsync(() => processor.Started == 4, "the signal ended the wait after the held batch");
        Stopwatch.GetElapsedTime(released, processor.StartTimestampOf(4)).ShouldBeLessThan(TimeSpan.FromMilliseconds(400),
            "the signal written during the batch was still pending: the next batch did not wait the 800 ms idle delay");
        var gap = processor.ClaimStartOf(4) - processor.ClaimStartOf(3);
        output.WriteLine($"floor-gap v4 gap={gap.TotalMilliseconds:F3}ms floor={options.PollingInterval.TotalMilliseconds}ms");
        gap.ShouldBeGreaterThanOrEqualTo(
            options.PollingInterval - Tolerance(options.PollingInterval), "no sooner than the floor after the held claim began");
    }

    // ---------------------------------------------------------------------------
    // D6 — only an idle or a partial batch's delay is cut
    // ---------------------------------------------------------------------------

    /// <summary>V5: the delay after a failed cycle (onFault) is not cut.</summary>
    [Fact]
    public async Task ASignalDuringAFailedCyclesDelay_DoesNotStartABatchBeforeItEnds()
    {
        var options = Options(polling: TimeSpan.FromMilliseconds(200));
        var wake = new LoopWake();
        var processor = new RecordingOutboxProcessor(wake, (n, _) => n == 1
            ? ValueTask.FromException<OutboxBatchResult>(new InvalidOperationException("The claim failed."))
            : ValueTask.FromResult(OutboxBatchResult.Empty));
        await using var loop = Run(processor, options, wake);

        await Eventually.HoldsAsync(() => processor.Ended == 1, "the first cycle failed");
        wake.Signal();

        await Eventually.HoldsAsync(() => processor.Started == 2, "the loop went on after its back-off");
        (processor.StartOf(2) - processor.EndOf(1)).ShouldBeGreaterThanOrEqualTo(
            options.PollingInterval * 2 - Tolerance(options.PollingInterval * 2), "the failure's back-off (400 ms) is not cut by a commit");
    }

    /// <summary>V5: the delay after an activation failure is not cut.</summary>
    [Fact]
    public async Task ASignalDuringAnActivationFailuresDelay_DoesNotStartABatchBeforeItEnds()
    {
        var options = Options(polling: TimeSpan.FromMilliseconds(200));
        var wake = new LoopWake();
        var processor = new RecordingOutboxProcessor(wake, (_, _) => ValueTask.FromResult(OutboxBatchResult.Empty));
        var attempts = new ConcurrentQueue<long>();
        await using var loop = Run(processor, options, wake, s => s.AddKeyedScoped<IOutboxProcessor>(Store, (_, _) =>
        {
            attempts.Enqueue(Stopwatch.GetTimestamp());
            return attempts.Count == 1 ? throw new InvalidOperationException("The store's constructor threw.") : processor;
        }));

        await Eventually.HoldsAsync(() => !attempts.IsEmpty, "the first activation failed");
        wake.Signal();

        await Eventually.HoldsAsync(() => processor.Started == 1, "the processor activated after the back-off");
        attempts.TryPeek(out var failed).ShouldBeTrue();
        Stopwatch.GetElapsedTime(failed, processor.StartTimestampOf(1)).ShouldBeGreaterThanOrEqualTo(
            options.PollingInterval * 2 - Tolerance(options.PollingInterval * 2), "the activation's back-off (400 ms) is not cut by a commit");
    }

    /// <summary>V5: the delay after a configuration fault is not cut.</summary>
    [Fact]
    public async Task ASignalDuringAConfigurationFaultsDelay_DoesNotStartABatchBeforeItEnds()
    {
        var options = Options(polling: TimeSpan.FromMilliseconds(200));
        var wake = new LoopWake();
        var processor = new RecordingOutboxProcessor(wake, (n, _) => n == 1
            ? ValueTask.FromException<OutboxBatchResult>(new OutboxConfigurationException("IOutboxDispatcher is not registered."))
            : ValueTask.FromResult(OutboxBatchResult.Empty));
        await using var loop = Run(processor, options, wake);

        await Eventually.HoldsAsync(() => processor.Ended == 1, "the first batch raised a configuration fault");
        wake.Signal();

        await Eventually.HoldsAsync(() => processor.Started == 2, "the loop went on after its back-off");
        (processor.StartOf(2) - processor.EndOf(1)).ShouldBeGreaterThanOrEqualTo(
            options.PollingInterval * 2 - Tolerance(options.PollingInterval * 2), "the configuration fault's back-off (400 ms) is not cut by a commit");
    }

    /// <summary>V5: the delay after an instance's preparation failure is not cut.</summary>
    [Fact]
    public async Task ASignalDuringAPreparationFailuresDelay_DoesNotStartABatchBeforeItEnds()
    {
        var options = Options(polling: TimeSpan.FromMilliseconds(200));
        var checks = new ConcurrentQueue<long>();
        var processor = new RecordingOutboxProcessor(null, (_, _) => ValueTask.FromResult(OutboxBatchResult.Empty));
        using var harness = new InstanceHarness(
            configure: s => s.AddKeyedScoped<IOutboxProcessor>(InstanceHarness.Store, (_, _) => processor),
            instanceCheck: _ =>
            {
                checks.Enqueue(Stopwatch.GetTimestamp());
                return checks.Count == 1 ? throw new InvalidOperationException("The instance's database is unreachable.") : [];
            });
        var instance = harness.Start("a");
        processor.Wake = instance.OutboxWake;
        using var host = new CancellationTokenSource();
        var run = new OutboxLoop(
            harness.ScopeFactory, InstanceHarness.Store, options, NullLogger<OutboxLoop>.Instance, instance, instance.OutboxWake)
            .RunAsync(host.Token);

        await Eventually.HoldsAsync(() => !checks.IsEmpty, "the first preparation failed");
        instance.OutboxWake.Signal();

        await Eventually.HoldsAsync(() => processor.Started == 1, "the instance was prepared after the back-off");
        checks.TryPeek(out var failed).ShouldBeTrue();
        Stopwatch.GetElapsedTime(failed, processor.StartTimestampOf(1)).ShouldBeGreaterThanOrEqualTo(
            options.PollingInterval * 2 - Tolerance(options.PollingInterval * 2), "the preparation's back-off (400 ms) is not cut by a commit");

        await host.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// V5: the hand-over guard's delay is not cut. The batch outlasts the floor, so a cut would start the
    /// next batch as soon as it returns.
    /// </summary>
    [Fact]
    public async Task ASignalDuringTheHandOverGuardsDelay_DoesNotStartABatchBeforeItEnds()
    {
        var options = Options(polling: TimeSpan.FromMilliseconds(200));
        var handedOver = new OutboxBatchResult(
            Claimed: 3, Published: 0, Retried: 0, DeadLettered: 0, Released: 3,
            OutboxBatchAbortReason.EndpointInBackOff) { HandedOverInBackOff = true };
        var wake = new LoopWake();
        var processor = new RecordingOutboxProcessor(wake, async (n, ct) =>
        {
            if (n == 1)
            {
                await Task.Delay(options.PollingInterval * 2, ct);
                return handedOver;
            }

            return OutboxBatchResult.Empty;
        });
        await using var loop = Run(processor, options, wake);

        await Eventually.HoldsAsync(() => processor.Started == 1, "the first batch is in flight");
        wake.Signal();

        await Eventually.HoldsAsync(() => processor.Started == 2, "the loop went on after the guard");
        (processor.StartOf(2) - processor.EndOf(1)).ShouldBeGreaterThanOrEqualTo(
            options.PollingInterval - Tolerance(options.PollingInterval), "the hand-over guard (200 ms after the batch) is not cut by a commit");
    }

    /// <summary>A partial batch's delay is cut, at the floor: D6's other wakeable arm.</summary>
    [Fact]
    public async Task ASignalDuringAPartialBatchsDelay_StartsTheNextBatchAtTheFloor()
    {
        var options = Options(polling: TimeSpan.FromMilliseconds(500));
        var partial = new OutboxBatchResult(
            Claimed: 3, Published: 3, Retried: 0, DeadLettered: 0, Released: 0, OutboxBatchAbortReason.None);
        var wake = new LoopWake();
        var processor = new RecordingOutboxProcessor(wake, async (n, ct) =>
        {
            if (n == 1)
            {
                // The batch takes most of the floor: the wake ends the delay well before its 500 ms.
                await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
                return partial;
            }

            return OutboxBatchResult.Empty;
        });
        await using var loop = Run(processor, options, wake);

        await Eventually.HoldsAsync(() => processor.Ended == 1, "the partial batch returned");
        wake.Signal();

        await Eventually.HoldsAsync(() => processor.Started == 2, "the signal ended the partial batch's delay");
        var gap = processor.ClaimStartOf(2) - processor.ClaimStartOf(1);
        output.WriteLine($"floor-gap partial gap={gap.TotalMilliseconds:F3}ms floor={options.PollingInterval.TotalMilliseconds}ms");
        gap.ShouldBeGreaterThanOrEqualTo(options.PollingInterval - Tolerance(options.PollingInterval), "the floor holds");
        (processor.StartOf(2) - processor.EndOf(1)).ShouldBeLessThan(
            options.PollingInterval - TimeSpan.FromMilliseconds(150), "the polling interval after the batch was cut");
    }

    // ---------------------------------------------------------------------------
    // Coalescing
    // ---------------------------------------------------------------------------

    /// <summary>V6: a hundred signals during one idle wait cost one extra batch, then the idle curve resumes.</summary>
    [Fact]
    public async Task ManySignalsDuringOneWait_CostOneBatch_AndTheIdleCurveGoesOn()
    {
        var options = Options(polling: TimeSpan.FromMilliseconds(100));
        var wake = new LoopWake();
        var processor = new RecordingOutboxProcessor(wake, (_, _) => ValueTask.FromResult(OutboxBatchResult.Empty));
        await using var loop = Run(processor, options, wake);

        // Three empty batches: the loop waits 800 ms.
        await Eventually.HoldsAsync(() => processor.Ended == 3, "the third empty batch returned");
        Parallel.For(0, 100, _ => wake.Signal());

        await Eventually.HoldsAsync(() => processor.Started == 4, "the signals woke the loop");
        (processor.StartOf(4) - processor.EndOf(3)).ShouldBeLessThan(TimeSpan.FromMilliseconds(400), "control: it was woken");

        await Eventually.HoldsAsync(() => processor.Started == 5, "the loop goes on");
        (processor.StartOf(5) - processor.EndOf(4)).ShouldBeGreaterThanOrEqualTo(
            TimeSpan.FromMilliseconds(1600) - Tolerance(TimeSpan.FromMilliseconds(1600)),
            "one batch absorbed every signal: the next waits the grown idle delay (1.6 s), not the floor");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static OutboxProcessorOptions Options(TimeSpan polling) => new()
    {
        BatchSize = 100,
        PollingInterval = polling,
        MaxPollingInterval = TimeSpan.FromMinutes(10),
        TransportUnavailableBackoff = TimeSpan.FromMinutes(10),
    };

    private static RunningLoop Run(
        RecordingOutboxProcessor processor,
        OutboxProcessorOptions options,
        LoopWake wake,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        if (configure is null)
        {
            services.AddKeyedScoped<IOutboxProcessor>(Store, (_, _) => processor);
        }
        else
        {
            configure(services);
        }

        var provider = services.BuildServiceProvider();
        var host = new CancellationTokenSource();
        var run = new OutboxLoop(
            provider.GetRequiredService<IServiceScopeFactory>(), Store, options, NullLogger<OutboxLoop>.Instance, wake: wake)
            .RunAsync(host.Token);
        return new RunningLoop(run, host, provider);
    }

    private sealed class RunningLoop(Task run, CancellationTokenSource host, ServiceProvider provider) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await host.CancelAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            host.Dispose();
            await provider.DisposeAsync();
        }
    }
}

/// <summary>
/// An outbox processor that runs a script per batch (1-based) and records when each batch started and
/// ended, on the monotonic clock the loop runs on. <see cref="StartOf"/> is the processor's entry;
/// <see cref="ClaimStartOf"/> is its loop's <see cref="LoopWake.ClaimStarted"/>, the floor's own origin,
/// for the floor assertions alone.
/// </summary>
internal sealed class RecordingOutboxProcessor(LoopWake? wake, Func<int, CancellationToken, ValueTask<OutboxBatchResult>> script) : IOutboxProcessor
{
    private readonly long _origin = Stopwatch.GetTimestamp();
    private readonly ConcurrentDictionary<int, long> _started = new();
    private readonly ConcurrentDictionary<int, long> _claimStarted = new();
    private readonly ConcurrentDictionary<int, long> _ended = new();
    private int _calls;
    private int _endedCount;

    /// <summary>Gets or sets the wake whose claim start is recorded; null records the processor's entry.</summary>
    public LoopWake? Wake { get; set; } = wake;

    /// <summary>Gets how many batches started.</summary>
    public int Started => Volatile.Read(ref _calls);

    /// <summary>Gets how many batches returned or threw.</summary>
    public int Ended => Volatile.Read(ref _endedCount);

    public TimeSpan StartOf(int batch) => Stopwatch.GetElapsedTime(_origin, _started[batch]);

    public TimeSpan ClaimStartOf(int batch) => Stopwatch.GetElapsedTime(_origin, _claimStarted[batch]);

    public long StartTimestampOf(int batch) => _started[batch];

    public TimeSpan EndOf(int batch) => Stopwatch.GetElapsedTime(_origin, _ended[batch]);

    public async ValueTask<OutboxBatchResult> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var n = Interlocked.Increment(ref _calls);
        _started[n] = Stopwatch.GetTimestamp();
        _claimStarted[n] = Wake?.ClaimStarted ?? _started[n];
        try
        {
            return await script(n, cancellationToken);
        }
        finally
        {
            _ended[n] = Stopwatch.GetTimestamp();
            Interlocked.Increment(ref _endedCount);
        }
    }
}
