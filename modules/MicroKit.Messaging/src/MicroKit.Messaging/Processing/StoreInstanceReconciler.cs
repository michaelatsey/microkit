using System.Threading.Channels;
using Microsoft.Extensions.Primitives;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// Keeps the instances the engine drains equal to a partitioned store's source (#191 D2, D5): reads
/// the source's snapshot at start and on every change, starts the four loops of each new instance,
/// and stops those of each removed one after their batches settle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Snapshot plus change token.</b> Each round takes the source's token first, registers on it, then
/// reads the snapshot and reconciles with it, so a change made after the read fires the token and a
/// change made before it is in the snapshot. Signals are coalesced in a bounded channel of one that
/// drops a write while one is pending (<see cref="ChannelWriter{T}.TryWrite"/> never throws): the
/// token's callback, which runs in the source's own code, cannot fail, and a missed or merged signal
/// loses nothing since every round reads the whole snapshot. A token that cannot be obtained, or that
/// raises no callbacks, is polled every <see cref="OutboxProcessorOptions.MaxPollingInterval"/>.
/// </para>
/// <para>
/// <b>A faulty snapshot changes nothing.</b> A source that throws, or returns one id twice, is logged
/// (1407, 1408); the instances already drained keep draining, and the next signal reads again.
/// </para>
/// <para>
/// <b>Lifecycle.</b> An added instance gets its own state — outbox and inbox back-off, metric series —
/// and its loops start (1404). A removed one stops claiming once its batches settle (1405), and its
/// state is released once its loops have ended (1406); re-added meanwhile, it starts again only then,
/// so one instance never has two sets of loops. A refused store (D7) starts no instance. At host stop
/// every loop ends on the host's token, as a single store's do, and the reconciler waits for them.
/// </para>
/// </remarks>
internal sealed class StoreInstanceReconciler
{
    private readonly MessageStoreKey _store;
    private readonly StoreInstanceRegistry _registry;
    private readonly InboxTenantBackoff _inbox;
    private readonly OutboxMetrics _metrics;
    private readonly OutboxProcessorOptions _outboxOptions;
    private readonly StoreInstanceOptions _instanceOptions;
    private readonly FifoGate _gate;
    private readonly Func<StoreInstanceState, CancellationToken, Task> _runLoops;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly Dictionary<MessageStoreInstance, StoreInstanceState> _running = [];
    private readonly Dictionary<MessageStoreInstance, StoreInstanceState> _draining = [];
    private int _rounds;
    private int _completedRounds;
    private int _changedTokensInARow;
    private bool _pollQuietly;

    /// <summary>Initializes a new <see cref="StoreInstanceReconciler"/>.</summary>
    /// <param name="store">The partitioned store.</param>
    /// <param name="registry">The instances drained, and the store's source.</param>
    /// <param name="inbox">Where each instance's inbox back-off is created and released.</param>
    /// <param name="metrics">Where each instance's outbox back-off is reported.</param>
    /// <param name="outboxOptions">The outbox back-off's curve, and the poll interval of a token with no callbacks.</param>
    /// <param name="instanceOptions">Whether instances are tagged.</param>
    /// <param name="gate">The gate every instance's batches pass.</param>
    /// <param name="runLoops">Runs an instance's four loops until they end.</param>
    /// <param name="timeProvider">The clock a poll waits on.</param>
    /// <param name="logger">Logger.</param>
    public StoreInstanceReconciler(
        MessageStoreKey store,
        StoreInstanceRegistry registry,
        InboxTenantBackoff inbox,
        OutboxMetrics metrics,
        OutboxProcessorOptions outboxOptions,
        StoreInstanceOptions instanceOptions,
        FifoGate gate,
        Func<StoreInstanceState, CancellationToken, Task> runLoops,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _store = store;
        _registry = registry;
        _inbox = inbox;
        _metrics = metrics;
        _outboxOptions = outboxOptions;
        _instanceOptions = instanceOptions;
        _gate = gate;
        _runLoops = runLoops;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Gets how many rounds have started, for tests: a round counts before it reads its snapshot.</summary>
    internal int Rounds => Volatile.Read(ref _rounds);

    /// <summary>
    /// Gets how many rounds have returned normally, for tests. Rounds run one after another, so once this
    /// reaches a value <see cref="Rounds"/> had, every round started by then has applied its snapshot.
    /// </summary>
    internal int CompletedRounds => Volatile.Read(ref _completedRounds);

    /// <summary>Gets or sets the count of changed tokens in a row, for tests: it saturates, never wraps.</summary>
    internal int ChangedTokensInARow
    {
        get => _changedTokensInARow;
        set => _changedTokensInARow = value;
    }

    /// <summary>Reconciles until the host stops, then waits for every instance's loops to end.</summary>
    /// <param name="stoppingToken">The host's token.</param>
    /// <returns>A task that completes once every instance's loops have ended.</returns>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ReconcileUntilStoppedAsync(stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            // Never leaves a delivery waiting for a first round that will not come.
            _registry.FirstRoundDone(_store);
        }

        var ending = new List<Task>(_running.Count + _draining.Count);
        foreach (var state in _running.Values)
        {
            ending.Add(state.Drained);
        }

        foreach (var state in _draining.Values)
        {
            ending.Add(state.Drained);
        }

        await Task.WhenAll(ending).ConfigureAwait(false);
    }

    private async Task ReconcileUntilStoppedAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var watched = Watch(out var registration);
            using (registration)
            {
                // A round that applied no snapshot — its read failed — leaves deliveries waiting (bounded),
                // and is retried on the poll interval, not only on the source's next change.
                var applied = Reconcile(stoppingToken);
                if (applied)
                {
                    _registry.FirstRoundDone(_store);
                }

                try
                {
                    if (_pollQuietly)
                    {
                        // Changed tokens in a row: one polling interval, deaf to their callbacks however
                        // late they come, then the source's token is taken again.
                        _pollQuietly = false;
                        await Task.Delay(_outboxOptions.PollingInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
                    }
                    else if (watched && applied)
                    {
                        await _signal.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await PollAsync(stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                while (_signal.Reader.TryRead(out _))
                {
                    // Coalesced: one round reads the whole snapshot.
                }
            }
        }
    }

    /// <summary>
    /// Waits for a signal or the poll interval, whichever comes first; the losing wait is cancelled, so no
    /// waiter stays queued on the channel round after round.
    /// </summary>
    private async Task PollAsync(CancellationToken stoppingToken)
    {
        using var round = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var signalled = _signal.Reader.WaitToReadAsync(round.Token).AsTask();
        var elapsed = Task.Delay(_outboxOptions.MaxPollingInterval, _timeProvider, round.Token);
        await Task.WhenAny(signalled, elapsed).ConfigureAwait(false);
        stoppingToken.ThrowIfCancellationRequested();
        await round.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(signalled, elapsed).ContinueWith(static _ => { }, TaskScheduler.Default).ConfigureAwait(false);
    }

    /// <summary>Asks for a round. Never throws, and never blocks: it runs in the source's own code.</summary>
    internal void Signal() => _signal.Writer.TryWrite(true);

    /// <summary>
    /// Takes the source's token and registers on it. False when there is nothing to wait on — the token
    /// raises no callbacks, or the source's token failed — and the round polls instead.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A source's fault is logged; the instances already drained keep draining.")]
    private bool Watch(out IDisposable? registration)
    {
        registration = null;
        try
        {
            var token = _registry.SourceOf(_store).GetChangeToken();
            if (token is not { ActiveChangeCallbacks: true })
            {
                return false;
            }

            registration = token.RegisterChangeCallback(static self => ((StoreInstanceReconciler)self!).Signal(), this);

            // A token that changed before the registration — its source cancelled, maybe disposed, in
            // between — may never call back: the next round takes the new one, at once. Another changed
            // token straight after, the same or a new one, is a source not replacing it: from then on
            // each is read one polling interval later, never in a loop. Only a live token resets this: a
            // round with no watchable token (none raising callbacks, or a faulty one) leaves the count as
            // it was, so a changed token right after such a round is read one polling interval late.
            if (!token.HasChanged)
            {
                _changedTokensInARow = 0;
                return true;
            }

            // Saturated, never wrapped: a wrap would read the source in a loop again.
            if (_changedTokensInARow < int.MaxValue)
            {
                _changedTokensInARow++;
            }

            if (_changedTokensInARow > 1)
            {
                registration.Dispose();
                registration = null;
                _pollQuietly = true;
                return false;
            }

            Signal();
            return true;
        }
        catch (Exception ex)
        {
            registration?.Dispose();
            registration = null;
            GuardedLoop.Log(() => StoreInstanceLogs.SourceFailed(_logger, ex, _store.Value));
            return false;
        }
    }

    /// <returns>Whether the round applied a snapshot: false when its read failed or named an instance twice.</returns>
    private bool Reconcile(CancellationToken stoppingToken)
    {
        Interlocked.Increment(ref _rounds);
        var applied = Apply(stoppingToken);
        Interlocked.Increment(ref _completedRounds);
        return applied;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A source's or an instance's fault is logged; the reconciler keeps running.")]
    private bool Apply(CancellationToken stoppingToken)
    {
        Forget(_draining);
        Forget(_running);

        // The snapshot is the source's code too — a null, a lazy collection that throws, a null entry:
        // read whole, inside the catch, before anything changes.
        HashSet<MessageStoreInstance> wanted;
        try
        {
            var snapshot = _registry.SourceOf(_store).GetInstances()
                ?? throw new InvalidOperationException("The source returned no snapshot.");
            wanted = new HashSet<MessageStoreInstance>(snapshot.Count);
            foreach (var instance in snapshot)
            {
                ArgumentNullException.ThrowIfNull(instance, "snapshot entry");
                if (!wanted.Add(instance))
                {
                    GuardedLoop.Log(() => StoreInstanceLogs.DuplicateInstance(_logger, _store.Value, instance.Id));
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            GuardedLoop.Log(() => StoreInstanceLogs.SourceFailed(_logger, ex, _store.Value));
            return false;
        }

        // A settling instance listed again, then removed again, maps no new delivery any more.
        foreach (var (instance, state) in _draining)
        {
            if (state.IsWanted && !wanted.Contains(instance))
            {
                state.IsWanted = false;
            }
        }

        List<MessageStoreInstance>? removed = null;
        foreach (var instance in _running.Keys)
        {
            if (!wanted.Contains(instance))
            {
                (removed ??= []).Add(instance);
            }
        }

        if (removed is not null)
        {
            foreach (var instance in removed)
            {
                var state = _running[instance];
                _running.Remove(instance);
                _draining[instance] = state;
                state.Remove();
                GuardedLoop.Log(() => StoreInstanceLogs.InstanceRemoved(_logger, _store.Value, instance.Id));
            }
        }

        foreach (var instance in wanted)
        {
            // A start can run the store's check and have it refuse the store — an instance initializer that
            // completes synchronously runs it before anything awaits: no instance starts after it.
            if (_registry.IsRefused(_store))
            {
                return true;
            }

            if (_running.ContainsKey(instance))
            {
                continue;
            }

            // Re-added while its loops still settle: it starts once they have ended and its state is
            // released, signalled then. Meanwhile its deliveries keep mapping to it.
            if (_draining.TryGetValue(instance, out var previous))
            {
                if (!previous.IsReleased)
                {
                    previous.IsWanted = true;
                    continue;
                }

                _draining.Remove(instance);
            }

            try
            {
                Start(instance, stoppingToken);
            }
            catch (Exception ex)
            {
                // Never the reconciler's end, nor the engine's: the instance is tried again next round.
                GuardedLoop.Log(() => StoreInstanceLogs.InstanceStartFailed(_logger, ex, _store.Value, instance.Id));
            }
        }

        return true;
    }

    private void Start(MessageStoreInstance instance, CancellationToken stoppingToken)
    {
        var key = new StoreInstanceKey(_store, instance.Id);
        var outbox = new OutboxEndpointBackoff(_outboxOptions.PollingInterval, _outboxOptions.TransportUnavailableBackoff);
        var tenants = _inbox.Add(key);
        _metrics.Track(key, outbox);
        var state = new StoreInstanceState(
            _store, instance, outbox, tenants, _gate, _registry, _instanceOptions.TagInstances, stoppingToken);
        _registry.Add(state);
        _running[instance] = state;
        GuardedLoop.Log(() => StoreInstanceLogs.InstanceStarted(_logger, _store.Value, instance.Id));
        state.Drained = DrainAsync(state, stoppingToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The loops never throw; if one did, its instance is still released and the engine keeps running.")]
    private async Task DrainAsync(StoreInstanceState state, CancellationToken stoppingToken)
    {
        try
        {
            await _runLoops(state, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            GuardedLoop.Log(() => StoreInstanceLogs.LoopFailed(
                _logger, ex, "engine", _store.Value, state.Instance.Id, TimeSpan.Zero));
        }
        catch (Exception)
        {
            // A stop: nothing to report.
        }
        finally
        {
            _registry.Remove(state);
            _metrics.Untrack(state.Key);
            _inbox.Release(state.Key);
            state.Dispose();
            GuardedLoop.Log(() => StoreInstanceLogs.InstanceReleased(_logger, _store.Value, state.Instance.Id));

            // Released before the signal, and read by the next round as such: a re-added instance waiting
            // for this release starts then, whether or not this task has completed yet.
            state.IsReleased = true;
            Signal();
        }
    }

    private static void Forget(Dictionary<MessageStoreInstance, StoreInstanceState> states)
    {
        List<MessageStoreInstance>? ended = null;
        foreach (var (instance, state) in states)
        {
            if (state.IsReleased)
            {
                (ended ??= []).Add(instance);
            }
        }

        if (ended is not null)
        {
            foreach (var instance in ended)
            {
                states.Remove(instance);
            }
        }
    }
}
