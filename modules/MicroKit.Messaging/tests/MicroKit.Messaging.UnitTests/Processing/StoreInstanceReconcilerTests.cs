using System.Collections.Concurrent;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// The reconciler of a partitioned store (#191 D2, D5, A3): it drains what the source's last snapshot
/// holds, whatever the signals, ignores a faulty snapshot, and runs one set of loops per instance.
/// </summary>
public sealed class StoreInstanceReconcilerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly string[] AandB = ["a", "b"];
    private const int SourceFailed = 1407;
    private const int InstanceStartFailed = 1415;

    [Fact]
    public async Task ManySignals_BeforeAndWhileItRuns_ThrowNothing_AndTheLastSnapshotIsWhatIsDrained()
    {
        using var harness = new InstanceHarness();
        var loops = new LoopRecorder();
        var sut = harness.Reconciler(loops.RunAsync);

        // Before it runs: nothing is registered on the token, and the signal channel is full after one.
        for (var i = 0; i < 1_000; i++)
        {
            sut.Signal();
        }

        using var stop = new CancellationTokenSource();
        var run = sut.RunAsync(stop.Token);

        // While it runs: every replacement fires the token its round registered on, from many threads.
        // Each callback writes to a channel of one that drops a write while one is pending: none throws
        // into the source's code (Cancel would surface it here).
        await Parallel.ForAsync(0, 1_000, async (i, _) =>
        {
            harness.Source.Set($"i{i % 7}", $"j{i % 3}");
            await Task.Yield();
        });
        harness.Source.Set("final-a", "final-b");

        await Eventually(() =>
            harness.Registry.Find(new StoreInstanceKey(InstanceHarness.Store, "final-a")) is not null
            && harness.Registry.Find(new StoreInstanceKey(InstanceHarness.Store, "final-b")) is not null
            && loops.Running.Count == 2,
            "the last snapshot is drained, and nothing else");

        sut.Rounds.ShouldBeGreaterThan(0);

        await stop.CancelAsync();
        await run.WaitAsync(Timeout);
        loops.Running.ShouldBeEmpty("at host stop every instance's loops end");
    }

    [Fact]
    public async Task AThrowingOrDuplicatedSnapshot_IsIgnored_AndTheInstancesDrainedKeepDraining()
    {
        using var harness = new InstanceHarness();
        var loops = new LoopRecorder();
        var sut = harness.Reconciler(loops.RunAsync);
        harness.Source.Set("a", "b");
        using var stop = new CancellationTokenSource();
        var run = sut.RunAsync(stop.Token);
        await Eventually(() => loops.Running.Count == 2, "control: both instances drained");

        harness.Source.ThrowOnRead = new InvalidOperationException("The tenant catalog is down.");
        harness.Source.Set("a");
        await RoundDone(sut, run, () => harness.Source.ThrowOnRead is null, "the throwing snapshot was read");
        loops.Running.Keys.Order(StringComparer.Ordinal).ToArray().ShouldBe(AandB, "a snapshot that throws changes nothing");

        harness.Source.Replace([new("a"), new("a")]);
        await SnapshotApplied(harness, sut, run, "the duplicated snapshot was read");
        loops.Running.Keys.Order(StringComparer.Ordinal).ToArray().ShouldBe(AandB, "a snapshot with one id twice changes nothing");

        harness.Source.Set("a");
        await Eventually(() => loops.Running.Count == 1, "the next good snapshot is applied");

        await stop.CancelAsync();
        await run.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ARemovedInstance_StopsAndIsReleased_AndReAddedStartsAgainOnlyOnceItsLoopsHaveEnded()
    {
        using var harness = new InstanceHarness();
        var loops = new LoopRecorder { EndOnStop = false };
        var sut = harness.Reconciler(loops.RunAsync);
        var key = new StoreInstanceKey(InstanceHarness.Store, "a");
        harness.Source.Set("a");
        using var stop = new CancellationTokenSource();
        var run = sut.RunAsync(stop.Token);
        await Eventually(() => loops.Starts("a") == 1, "control: drained");
        var first = loops.Running["a"];

        // Removed while its "batch" is in flight: it is asked to stop, no new delivery maps to it, and its
        // state stays until its loops end.
        harness.Source.Set();
        await Eventually(() => first.IsRemoved, "asked to stop");
        first.Stopping.IsCancellationRequested.ShouldBeTrue();
        harness.Registry.Find(key).ShouldBeNull("a removed instance is not drained");
        harness.Inbox.Stores.ShouldContain(e => e.Key == key, "its state outlives its loops' last batch");

        // Re-added before its loops have ended: not started a second time alongside them.
        harness.Source.Set("a");
        await SnapshotApplied(harness, sut, run, "the re-adding snapshot was read");
        loops.Starts("a").ShouldBe(1, "one instance never runs two sets of loops");
        harness.Registry.Find(key).ShouldBeSameAs(first, "listed again, its deliveries keep mapping to it until its successor starts");

        // Its loops end: released, then started again.
        loops.End("a");
        await Eventually(() => loops.Starts("a") == 2, "re-added: drained again once released");
        var second = loops.Running["a"];
        second.ShouldNotBeSameAs(first, "a fresh state, released and created again");
        harness.Inbox.Stores.Count(e => e.Key == key).ShouldBe(1);

        harness.Source.Set();
        await Eventually(() => second.IsRemoved, "removed again");
        loops.End("a");
        await Eventually(() => !harness.Inbox.Stores.Any(e => e.Key == key), "released: no inbox state is left");

        await stop.CancelAsync();
        await run.WaitAsync(Timeout);
    }

    [Fact]
    public async Task AnInstanceReAddedThenRemovedAgain_WhileItsLoopsSettle_MapsNoDelivery()
    {
        using var harness = new InstanceHarness();
        var loops = new LoopRecorder { EndOnStop = false };
        var sut = harness.Reconciler(loops.RunAsync);
        var key = new StoreInstanceKey(InstanceHarness.Store, "a");
        harness.Source.Set("a");
        using var stop = new CancellationTokenSource();
        var run = sut.RunAsync(stop.Token);
        await Eventually(() => loops.Starts("a") == 1, "control: drained");
        var first = loops.Running["a"];

        harness.Source.Set();
        await Eventually(() => first.IsRemoved, "removed");
        harness.Source.Set("a");
        await Eventually(() => harness.Registry.Find(key) is not null, "listed again: its deliveries map to it");
        harness.Source.Set();
        await Eventually(() => harness.Registry.Find(key) is null, "removed again: they no longer do");

        loops.End("a");
        await Eventually(() => !harness.Inbox.Stores.Any(e => e.Key == key), "released");
        loops.Starts("a").ShouldBe(1, "not listed when its loops ended: not started again");

        await stop.CancelAsync();
        await run.WaitAsync(Timeout);
    }

    [Fact]
    public async Task AFaultyTokenOrSnapshot_IsLogged_AndNeverEndsTheReconciler()
    {
        var faulty = new FaultySource();
        using var harness = new InstanceHarness(configure: s => s.AddKeyedSingleton<IMessageStoreInstanceSource>(
            InstanceHarness.Store, (sp, _) => faulty.Over(sp.GetRequiredService<TestInstanceSource>())));
        var loops = new LoopRecorder();
        var log = new EventLog();
        var sut = harness.Reconciler(loops.RunAsync, log);
        harness.Source.Set("a");
        using var stop = new CancellationTokenSource();
        var run = sut.RunAsync(stop.Token);
        await Eventually(() => loops.Starts("a") == 1, "control: drained");

        foreach (var fault in new[] { Fault.Registration, Fault.NullSnapshot, Fault.ThrowingEnumerator })
        {
            var logged = log.Count(SourceFailed);
            faulty.Next = fault;
            harness.Source.Set("a", "b");
            var version = harness.Source.Version;

            // A registration fault is met in Watch, before its round starts: alone it is not something only a
            // round does, so it is paired with the snapshot read, which that round still makes.
            await RoundDone(
                sut,
                run,
                () => faulty.Next == Fault.None && (fault != Fault.Registration || harness.Source.ReadVersion >= version),
                $"the {fault} was met in a round");
            run.IsCompleted.ShouldBeFalse($"a {fault} ends neither the reconciler nor the engine");
            log.Count(SourceFailed).ShouldBe(logged + 1, $"a {fault} is logged once ({SourceFailed})");
        }

        faulty.Next = Fault.None;
        harness.Source.Set("a", "b", "c");
        await Eventually(() => loops.Starts("c") == 1, "a good snapshot is applied after the faults");

        await stop.CancelAsync();
        await run.WaitAsync(Timeout);
    }

    [Fact]
    public async Task AnInstanceThatCannotStart_IsLogged_AndTheReconcilerKeepsRunning()
    {
        using var harness = new InstanceHarness();
        var loops = new LoopRecorder();
        var log = new EventLog();
        var sut = harness.Reconciler(loops.RunAsync, log);
        var key = new StoreInstanceKey(InstanceHarness.Store, "a");
        harness.Inbox.Add(key);   // state nobody released: starting "a" throws
        harness.Source.Set("a", "b");
        using var stop = new CancellationTokenSource();
        var run = sut.RunAsync(stop.Token);

        await SnapshotApplied(harness, sut, run, "the first snapshot was applied");
        loops.Starts("b").ShouldBe(1, "the other instance starts");
        loops.Starts("a").ShouldBe(0);
        log.Count(InstanceStartFailed).ShouldBe(1, $"the failed start is logged ({InstanceStartFailed})");

        harness.Inbox.Release(key);
        harness.Source.Set("a", "b");
        await Eventually(() => loops.Starts("a") == 1, "tried again on the next change");

        await stop.CancelAsync();
        await run.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ATokenThatChangedBeforeItsRegistration_IsNotWaitedOn_AndLaterChangesAreSeen()
    {
        using var harness = new InstanceHarness(configure: s => s.AddKeyedSingleton<IMessageStoreInstanceSource>(
            InstanceHarness.Store, (sp, _) => new DeadFirstTokenSource(sp.GetRequiredService<TestInstanceSource>())));
        var loops = new LoopRecorder();
        var sut = harness.Reconciler(loops.RunAsync);
        harness.Source.Set("a");
        using var stop = new CancellationTokenSource();
        var run = sut.RunAsync(stop.Token);
        await Eventually(() => loops.Starts("a") == 1, "control: the first snapshot is drained");

        harness.Source.Set("a", "b");
        await Eventually(() => loops.Starts("b") == 1, "a change after a dead first token is still seen");

        await stop.CancelAsync();
        await run.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ATokenThatRaisesNoCallbacks_IsPolled()
    {
        using var harness = new InstanceHarness(configure: s => s.AddKeyedSingleton<IMessageStoreInstanceSource>(
            InstanceHarness.Store, (sp, _) => new PassiveTokenSource(sp.GetRequiredService<TestInstanceSource>())));
        var loops = new LoopRecorder();
        var sut = harness.Reconciler(loops.RunAsync);
        using var stop = new CancellationTokenSource();
        var run = sut.RunAsync(stop.Token);

        harness.Source.Set("a");
        await Eventually(() => loops.Starts("a") == 1, "read again at the poll interval");
        var rounds = sut.Rounds;
        await Eventually(() => sut.Rounds >= rounds + 3, "it keeps polling");

        await stop.CancelAsync();
        await run.WaitAsync(Timeout);
    }

    /// <summary>
    /// Waits until <paramref name="metInARound"/> holds, then until that round has returned: rounds run one
    /// after another, so every round started by then has. <paramref name="metInARound"/> must be something
    /// only a round does once it has started (<see cref="StoreInstanceReconciler.Rounds"/> counts it before
    /// its snapshot read); a fault met in <c>Watch</c>, before the round starts, is paired with the snapshot
    /// read. A reconciler that ended — a round that threw — fails the wait at once, with its exception.
    /// </summary>
    private static async Task RoundDone(StoreInstanceReconciler sut, Task run, Func<bool> metInARound, string because)
    {
        await Eventually(() => run.IsCompleted || metInARound(), because);
        await ThrowIfEnded(run, because);
        var started = sut.Rounds;
        await Eventually(() => run.IsCompleted || sut.CompletedRounds >= started, $"{because}, and its round returned");
        await ThrowIfEnded(run, because);
    }

    /// <summary>Waits until the source's latest snapshot has been read and its round has returned.</summary>
    private static Task SnapshotApplied(InstanceHarness harness, StoreInstanceReconciler sut, Task run, string because)
    {
        var version = harness.Source.Version;
        return RoundDone(sut, run, () => harness.Source.ReadVersion >= version, because);
    }

    private static async Task ThrowIfEnded(Task run, string because)
    {
        if (run.IsCompleted)
        {
            await run;
            throw new ShouldAssertException($"The reconciler ended while waiting: {because}");
        }
    }

    private static async Task Eventually(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new ShouldAssertException($"Timed out waiting: {because}");
            }

            await Task.Delay(5);
        }
    }

    /// <summary>
    /// Hands out, the first time, a token that has changed and whose registration never calls back — what
    /// <c>CancellationChangeToken</c> does when its source was disposed before the registration.
    /// </summary>
    private sealed class DeadFirstTokenSource(TestInstanceSource inner) : IMessageStoreInstanceSource
    {
        private int _calls;

        public IReadOnlyCollection<MessageStoreInstance> GetInstances() => inner.GetInstances();

        public Microsoft.Extensions.Primitives.IChangeToken GetChangeToken()
        {
            if (Interlocked.Increment(ref _calls) > 1)
            {
                return inner.GetChangeToken();
            }

            return new Dead();
        }

        private sealed class Dead : Microsoft.Extensions.Primitives.IChangeToken, IDisposable
        {
            public bool HasChanged => true;

            public bool ActiveChangeCallbacks => true;

            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => this;

            public void Dispose()
            {
            }
        }

        public bool TryGetInstance(MessageContext context, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MessageStoreInstance? instance)
            => inner.TryGetInstance(context, out instance);
    }

    private enum Fault
    {
        None,
        Registration,
        NullSnapshot,
        ThrowingEnumerator,
    }

    /// <summary>Counts the events written, by id; written on the reconciler's thread, read on the test's.</summary>
    private sealed class EventLog : ILogger
    {
        private readonly ConcurrentDictionary<int, int> _counts = new();

        public int Count(int eventId) => _counts.GetValueOrDefault(eventId);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        // Always enabled: the [LoggerMessage] guard would otherwise make the call vanish.
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _counts.AddOrUpdate(eventId.Id, 1, static (_, n) => n + 1);
    }

    /// <summary>A source that, once, throws on registration, returns no snapshot, or a snapshot that throws.</summary>
    private sealed class FaultySource
    {
        // Written by the test thread and taken on the reconciler's: volatile.
        private volatile Fault _next;

        public Fault Next
        {
            get => _next;
            set => _next = value;
        }

        public IMessageStoreInstanceSource Over(TestInstanceSource inner) => new Wrapper(this, inner);

        private Fault Take()
        {
            var fault = Next;
            Next = Fault.None;
            return fault;
        }

        private sealed class Wrapper(FaultySource owner, TestInstanceSource inner) : IMessageStoreInstanceSource
        {
            public IReadOnlyCollection<MessageStoreInstance> GetInstances() => owner.Next switch
            {
                Fault.NullSnapshot => (owner.Take() is var _ ? null : null)!,
                Fault.ThrowingEnumerator => owner.Take() is var _ ? new Throwing() : null!,
                _ => inner.GetInstances(),
            };

            public Microsoft.Extensions.Primitives.IChangeToken GetChangeToken()
                => owner.Next == Fault.Registration ? new ThrowingToken(owner) : inner.GetChangeToken();

            public bool TryGetInstance(MessageContext context, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MessageStoreInstance? instance)
                => inner.TryGetInstance(context, out instance);
        }

        private sealed class ThrowingToken(FaultySource owner) : Microsoft.Extensions.Primitives.IChangeToken
        {
            public bool HasChanged => false;

            public bool ActiveChangeCallbacks => true;

            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
            {
                owner.Take();
                throw new InvalidOperationException("The token cannot be registered on.");
            }
        }

        private sealed class Throwing : IReadOnlyCollection<MessageStoreInstance>
        {
            public int Count => 1;

            public IEnumerator<MessageStoreInstance> GetEnumerator() => throw new InvalidOperationException("The snapshot cannot be read.");

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }

    /// <summary>A source whose token raises no callbacks: the reconciler must poll it.</summary>
    private sealed class PassiveTokenSource(TestInstanceSource inner) : IMessageStoreInstanceSource
    {
        public IReadOnlyCollection<MessageStoreInstance> GetInstances() => inner.GetInstances();

        public Microsoft.Extensions.Primitives.IChangeToken GetChangeToken() => new Passive();

        public bool TryGetInstance(MessageContext context, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MessageStoreInstance? instance)
            => inner.TryGetInstance(context, out instance);

        private sealed class Passive : Microsoft.Extensions.Primitives.IChangeToken
        {
            public bool HasChanged => false;

            public bool ActiveChangeCallbacks => false;

            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => throw new InvalidOperationException("Never registered on.");
        }
    }

    /// <summary>Stands for an instance's four loops: running until stopped, or until the test ends them.</summary>
    private sealed class LoopRecorder
    {
        private readonly ConcurrentDictionary<string, int> _starts = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _ends = new();

        /// <summary>Gets or sets whether the loops end as soon as they are asked to stop.</summary>
        public bool EndOnStop { get; init; } = true;

        public ConcurrentDictionary<string, StoreInstanceState> Running { get; } = new();

        public int Starts(string id) => _starts.GetValueOrDefault(id);

        public void End(string id) => _ends[id].TrySetResult();

        public async Task RunAsync(StoreInstanceState state, CancellationToken stoppingToken)
        {
            var id = state.Instance.Id;
            var end = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ends[id] = end;
            Running[id] = state;
            _starts.AddOrUpdate(id, 1, static (_, n) => n + 1);
            try
            {
                if (EndOnStop)
                {
                    await Task.Delay(Timeout * 6, state.Stopping).ContinueWith(static _ => { }, TaskScheduler.Default);
                }
                else
                {
                    // A batch in flight that settles on its own time, not on the removal.
                    await Task.WhenAny(end.Task, Task.Delay(Timeout * 6, stoppingToken)).ContinueWith(static _ => { }, TaskScheduler.Default);
                }
            }
            finally
            {
                Running.TryRemove(new KeyValuePair<string, StoreInstanceState>(id, state));
            }
        }
    }
}
