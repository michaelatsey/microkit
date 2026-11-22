namespace MicroKit.Messaging.Processing;

/// <summary>
/// Bounds the batches in progress across every instance of every partitioned store (#191 D6), and
/// admits waiting batches in arrival order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <see cref="SemaphoreSlim"/>.</b> It documents no order for its waiters, and a freed
/// permit can be taken by a newcomer ahead of a waiter: an instance whose loop asks again as soon as
/// its batch settles — a large backlog — could pass a waiting instance indefinitely. Here a released
/// permit is handed to the oldest waiter directly; a newcomer gets a free permit only when nobody
/// waits, so <c>available &gt; 0</c> implies an empty queue.
/// </para>
/// <para>
/// <b>One arbiter.</b> A release and a cancellation race on a waiter; the gate's lock and the waiter's
/// presence in the queue decide it. A release removes the waiter it hands the permit to: that waiter
/// owns the permit, even if its token fires afterwards, and its wait completes successfully. A
/// cancellation removes a waiter still queued: it never receives one. So no permit is ever handed to a
/// waiter that will not use it, and none is lost. Every successful <see cref="EnterAsync"/> owes exactly
/// one <see cref="Release"/>, in a <c>finally</c>.
/// </para>
/// </remarks>
internal sealed class FifoGate
{
    private readonly Lock _lock = new();
    private readonly LinkedList<Waiter> _waiters = new();
    private int _available;

    /// <summary>Initializes a new <see cref="FifoGate"/>.</summary>
    /// <param name="permits">How many holders it admits at once; at least 1.</param>
    public FifoGate(int permits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permits, 1);
        Permits = permits;
        _available = permits;
    }

    /// <summary>Gets how many holders the gate admits at once.</summary>
    public int Permits { get; }

    /// <summary>Gets the permits nobody holds, for tests.</summary>
    internal int Available
    {
        get
        {
            lock (_lock)
            {
                return _available;
            }
        }
    }

    /// <summary>Gets the waiters queued, for tests.</summary>
    internal int Waiting
    {
        get
        {
            lock (_lock)
            {
                return _waiters.Count;
            }
        }
    }

    /// <summary>Waits for a permit, behind every earlier waiter.</summary>
    /// <param name="ct">Withdraws the wait; a permit already handed over is kept.</param>
    /// <returns>A task that completes once the caller holds a permit.</returns>
    /// <exception cref="OperationCanceledException">The wait was withdrawn before a permit was handed over.</exception>
    public async ValueTask EnterAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        Waiter waiter;
        lock (_lock)
        {
            if (_available > 0)
            {
                _available--;
                return;
            }

            waiter = new Waiter(this);
            waiter.Node = _waiters.AddLast(waiter);
        }

        // Registered after the waiter is queued: a token already cancelled runs the callback here,
        // which withdraws it. Disposed once the wait is over, whichever way it ended.
        using (ct.UnsafeRegister(static (state, token) => ((Waiter)state!).Withdraw(token), waiter))
        {
            await waiter.Completion.ConfigureAwait(false);
        }
    }

    /// <summary>Returns a permit: to the oldest waiter, or to the gate when nobody waits.</summary>
    /// <exception cref="InvalidOperationException">More permits were returned than were handed out.</exception>
    public void Release()
    {
        Waiter next;
        lock (_lock)
        {
            if (_waiters.First is not { } first)
            {
                if (_available >= Permits)
                {
                    throw new InvalidOperationException("The gate was released more times than it was entered.");
                }

                _available++;
                return;
            }

            _waiters.RemoveFirst();
            next = first.Value;
            next.Node = null;
        }

        // Outside the lock: the waiter's continuation runs asynchronously anyway.
        next.Grant();
    }

    /// <summary>Removes a waiter still queued; false once a release has taken it.</summary>
    private bool TryWithdraw(Waiter waiter)
    {
        lock (_lock)
        {
            if (waiter.Node is not { } node)
            {
                return false;
            }

            _waiters.Remove(node);
            waiter.Node = null;
            return true;
        }
    }

    private sealed class Waiter(FifoGate gate)
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets or sets the waiter's place in the queue; null once handed a permit or withdrawn. Guarded by the gate's lock.</summary>
        public LinkedListNode<Waiter>? Node { get; set; }

        public Task Completion => _completion.Task;

        public void Grant() => _completion.TrySetResult();

        public void Withdraw(CancellationToken token)
        {
            if (gate.TryWithdraw(this))
            {
                _completion.TrySetCanceled(token);
            }
        }
    }
}
