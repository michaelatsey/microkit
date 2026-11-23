using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The partitioned stores of the composition and the instances the engine drains now (#191): what a
/// delivery guarded by a partitioned store is mapped to, and each store's check.
/// </summary>
/// <remarks>
/// <para>
/// <b>A lookup never creates.</b> <see cref="Find(MessageStoreKey, MessageContext)"/> maps a delivery
/// through the store's source, then reads the instances drained: an instance absent there, removed,
/// or of a refused store is not drained, and the delivery gets no state at all. Only the reconciler adds
/// and removes instances (<see cref="Add"/>, <see cref="Remove"/>).
/// </para>
/// <para>
/// <b>The check (D7), once per store.</b> A partitioned store's instance checks run in the prepared
/// scope of the first loop that starts, one at a time. Problems are the store's: logged once at
/// Critical (1409), and no instance of the store starts until a restart. A throw is the instance's: the
/// loop backs off, and the check stays pending for the next attempt.
/// </para>
/// <para>A singleton, registered by <c>AddMicroKitMessaging()</c>; empty on a host with no partitioned store.</para>
/// </remarks>
internal sealed class StoreInstanceRegistry
{
    private readonly FrozenDictionary<MessageStoreKey, Partition> _partitions;
    private readonly ConcurrentDictionary<StoreInstanceKey, StoreInstanceState> _drained = new();
    private readonly IServiceProvider _services;
    private readonly ILogger<StoreInstanceRegistry> _logger;
    private readonly TimeSpan _firstRoundWait;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the registry over the composition's partitioned stores.</summary>
    /// <param name="composition">The composition.</param>
    /// <param name="services">The root provider, which holds each store's keyed source.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="outboxOptions">
    /// Its <see cref="OutboxProcessorOptions.PollingInterval"/> bounds a delivery's wait for a first snapshot.
    /// </param>
    /// <param name="timeProvider">The clock of that wait.</param>
    public StoreInstanceRegistry(
        MessagingComposition composition,
        IServiceProvider services,
        ILogger<StoreInstanceRegistry> logger,
        OutboxProcessorOptions? outboxOptions = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(composition);
        _services = services;
        _logger = logger;
        _firstRoundWait = (outboxOptions ?? new OutboxProcessorOptions()).PollingInterval;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _partitions = composition.Stores
            .Where(static s => s.IsPartitioned)
            .ToFrozenDictionary(static s => s.Key, static s => new Partition(s.Key, [.. s.InstanceChecks]));
        PartitionedStores = [.. _partitions.Keys];
    }

    /// <summary>Gets the partitioned stores, in no particular order.</summary>
    public MessageStoreKey[] PartitionedStores { get; }

    /// <summary>Gets a value indicating whether any store is partitioned: one read on a host with none.</summary>
    public bool HasPartitions => _partitions.Count > 0;

    /// <summary>Whether <paramref name="store"/> is partitioned.</summary>
    /// <param name="store">The store.</param>
    /// <returns><see langword="true"/> when its instances come from a source.</returns>
    public bool IsPartitioned(MessageStoreKey store) => _partitions.ContainsKey(store);

    /// <summary>The source of a partitioned store's instances.</summary>
    /// <param name="store">The store.</param>
    /// <returns>Its keyed singleton source.</returns>
    public IMessageStoreInstanceSource SourceOf(MessageStoreKey store) => _partitions[store].Source(_services);

    /// <summary>Whether a partitioned store was refused by its check: no instance of it is drained.</summary>
    /// <param name="store">The store.</param>
    /// <returns><see langword="true"/> once its check returned problems.</returns>
    public bool IsRefused(MessageStoreKey store)
        => _partitions.TryGetValue(store, out var partition) && partition.Refused;

    /// <summary>The instance a delivery guarded by <paramref name="store"/> is mapped to, if it is drained.</summary>
    /// <param name="store">The partitioned store.</param>
    /// <param name="context">The delivery.</param>
    /// <returns>The drained instance; <see langword="null"/> when it maps to none this host drains.</returns>
    /// <remarks>
    /// A source that throws, or answers true with no instance, maps nothing: the delivery is not drained
    /// (1416), never a failure of the pipeline.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A source's fault makes one delivery undrained; it must not escape the pipeline.")]
    public StoreInstanceState? Find(MessageStoreKey store, MessageContext context)
    {
        if (!_partitions.TryGetValue(store, out var partition) || partition.Refused)
        {
            return null;
        }

        MessageStoreInstance? instance;
        try
        {
            if (!partition.Source(_services).TryGetInstance(context, out instance))
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            StoreInstanceLogs.MappingFailed(_logger, ex, store.Value, context.MessageId.Value);
            return null;
        }

        // [NotNullWhen(true)] is the source's promise, not the runtime's.
        if (instance is null)
        {
            StoreInstanceLogs.MappingFailed(_logger, null, store.Value, context.MessageId.Value);
            return null;
        }

        return Find(new StoreInstanceKey(store, instance.Id));
    }

    /// <summary>
    /// Completes once the reconciler of <paramref name="store"/> has applied its first snapshot (or ended);
    /// <see cref="WaitForFirstRoundAsync"/> is the delivery's bounded wait on it.
    /// </summary>
    /// <param name="store">The partitioned store.</param>
    /// <returns>The task; completed for a store that is not partitioned.</returns>
    public Task FirstRoundOf(MessageStoreKey store)
        => _partitions.TryGetValue(store, out var partition) ? partition.FirstRound.Task : Task.CompletedTask;

    /// <summary>
    /// Waits for the first snapshot of <paramref name="store"/> to be applied — at most one
    /// <see cref="OutboxProcessorOptions.PollingInterval"/>: a reconciler that cannot read its source, or
    /// never runs, holds no delivery longer, which then maps as the registry stands (1410 if to nothing).
    /// </summary>
    /// <param name="store">The partitioned store.</param>
    /// <param name="ct">The delivery's token.</param>
    /// <returns>A task that completes once the snapshot is applied or the bound has passed.</returns>
    /// <remarks>Once the snapshot is applied, a completed task, with no allocation.</remarks>
    public async ValueTask WaitForFirstRoundAsync(MessageStoreKey store, CancellationToken ct = default)
    {
        if (!_partitions.TryGetValue(store, out var partition) || partition.FirstRound.Task.IsCompleted)
        {
            return;
        }

        try
        {
            await partition.FirstRound.Task.WaitAsync(_firstRoundWait, _timeProvider, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Bounded: the delivery maps as the registry stands.
        }
    }

    /// <summary>Marks the first round of <paramref name="store"/> done; later calls do nothing.</summary>
    /// <param name="store">The partitioned store.</param>
    public void FirstRoundDone(MessageStoreKey store)
    {
        if (_partitions.TryGetValue(store, out var partition))
        {
            partition.FirstRound.TrySetResult();
        }
    }

    /// <summary>A drained instance, by key; null when it is not drained.</summary>
    /// <param name="key">The instance.</param>
    /// <returns>Its state, or <see langword="null"/>.</returns>
    public StoreInstanceState? Find(StoreInstanceKey key)
        => _drained.TryGetValue(key, out var state) && !state.IsRemoved ? state : null;

    /// <summary>Records an instance the reconciler starts draining.</summary>
    /// <param name="state">Its state.</param>
    public void Add(StoreInstanceState state) => _drained[state.Key] = state;

    /// <summary>Forgets an instance whose loops have ended.</summary>
    /// <param name="state">Its state; another state under the same key is left alone.</param>
    public void Remove(StoreInstanceState state)
        => _drained.TryRemove(new KeyValuePair<StoreInstanceKey, StoreInstanceState>(state.Key, state));

    /// <summary>Runs the store's check if it is still pending, in an instance's prepared scope (D7).</summary>
    /// <param name="instance">The instance whose loop asks.</param>
    /// <param name="services">Its prepared scope's provider.</param>
    /// <returns><see langword="true"/> once the check passed; <see langword="false"/> once it refused the store.</returns>
    /// <exception cref="Exception">The check threw: the instance's own failure, and the check stays pending.</exception>
    public bool EnsureChecked(StoreInstanceState instance, IServiceProvider services)
    {
        var partition = _partitions[instance.Store];
        return partition.State switch
        {
            Partition.Passed => true,
            Partition.Failed => false,
            _ => partition.Check(instance, services, _logger),
        };
    }

    /// <summary>One partitioned store: its source, its checks and their verdict.</summary>
    private sealed class Partition(MessageStoreKey store, Func<IServiceProvider, IEnumerable<string>>[] checks)
    {
        public const int Pending = 0;
        public const int Passed = 1;
        public const int Failed = 2;

        private readonly Lock _checking = new();
        private IMessageStoreInstanceSource? _source;
        private int _state = checks.Length == 0 ? Passed : Pending;

        public int State => Volatile.Read(ref _state);

        public TaskCompletionSource FirstRound { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Refused => State == Failed;

        public IMessageStoreInstanceSource Source(IServiceProvider services)
            => Volatile.Read(ref _source)
               ?? (_source = services.GetRequiredKeyedService<IMessageStoreInstanceSource>(store));

        public bool Check(StoreInstanceState instance, IServiceProvider services, ILogger logger)
        {
            // One check at a time: the first instance's four loops may all ask at once.
            lock (_checking)
            {
                if (State != Pending)
                {
                    return State == Passed;
                }

                var problems = new List<string>();
                foreach (var check in checks)
                {
                    problems.AddRange(check(services));
                }

                if (problems.Count == 0)
                {
                    Volatile.Write(ref _state, Passed);
                    return true;
                }

                Volatile.Write(ref _state, Failed);
                GuardedLoop.Log(() => StoreInstanceLogs.StoreRefused(
                    logger, store.Value, instance.Instance.Id, string.Join(" ", problems)));
                return false;
            }
        }
    }
}
