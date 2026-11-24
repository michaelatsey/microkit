using System.Threading.Channels;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The wake of one outbox loop (#215): a commit signals it, and the loop's idle wait ends — no sooner
/// than <see cref="OutboxProcessorOptions.PollingInterval"/> after its previous claim began, to the timer's
/// precision.
/// </summary>
/// <remarks>
/// <para>
/// <b>The signal side</b> (<see cref="Signal"/>) runs on any thread, in a store provider's code at the
/// end of a scope: a bounded channel of one that drops a write while one is pending, never completed,
/// so it never throws, never blocks, and coalesces any number of commits into one wake. Its
/// continuations are asynchronous: the loop never resumes on the committing thread.
/// </para>
/// <para>
/// <b>The loop side</b> belongs to the one loop that owns this wake, and runs on its flow alone. Right
/// before each claim the loop drains the signal and records when the claim began
/// (<see cref="ClaimStarting"/>), so a commit that lands during the batch stays pending and ends the
/// next wait at once. After an idle or partial batch it arms the wake (<see cref="Arm"/>):
/// <see cref="GuardedLoop"/> ends only an armed delay on a signal, and disarms before every iteration.
/// The claim's start and the floor are read on the clock <see cref="GuardedLoop"/> waits on
/// (<see cref="Bind"/>), never another.
/// </para>
/// </remarks>
internal sealed class LoopWake
{
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });

    private TimeProvider _clock = TimeProvider.System;
    private long _claimStarted;
    private TimeSpan _floor;

    /// <summary>Gets the signal, read by <see cref="GuardedLoop"/> while it waits.</summary>
    internal ChannelReader<bool> Reader => _signal.Reader;

    /// <summary>Gets the timestamp of the last claim's start, on the bound clock: the floor's origin.</summary>
    internal long ClaimStarted => _claimStarted;

    /// <summary>Gets a value indicating whether this iteration's delay may be cut by a signal.</summary>
    internal bool Armed { get; private set; }

    /// <summary>Asks for a claim. Never throws and never blocks: it runs in a store provider's code.</summary>
    public void Signal() => _signal.Writer.TryWrite(true);

    /// <summary>Sets the clock the claim's start and the floor are read on: the one the loop waits on.</summary>
    /// <param name="clock">The loop's clock.</param>
    internal void Bind(TimeProvider clock) => _clock = clock;

    /// <summary>
    /// A claim is about to start: drains the signal — a commit already signalled is visible to this
    /// claim — and records the claim's start, from which the floor is measured.
    /// </summary>
    internal void ClaimStarting()
    {
        while (_signal.Reader.TryRead(out _))
        {
            // Coalesced: this claim reads every row committed so far.
        }

        _claimStarted = _clock.GetTimestamp();
    }

    /// <summary>This iteration's delay may be cut, no sooner than <paramref name="floor"/> after the claim's start.</summary>
    /// <param name="floor">The least time between the starts of two claims.</param>
    internal void Arm(TimeSpan floor)
    {
        _floor = floor;
        Armed = true;
    }

    /// <summary>Disarms the wake: an iteration's delay is not cut unless that iteration arms it.</summary>
    internal void Disarm() => Armed = false;

    /// <summary>Gets what remains of the floor: zero or less once it has passed.</summary>
    internal TimeSpan FloorRemaining => _floor - _clock.GetElapsedTime(_claimStarted);
}
