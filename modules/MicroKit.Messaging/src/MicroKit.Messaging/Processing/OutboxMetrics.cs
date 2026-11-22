using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The outbox's instruments (#126), on the meter named by <see cref="MessagingDiagnostics.OutboxMeterName"/>:
/// what each batch settled, how long a row waited before its endpoint took it, and which endpoints and
/// tenants have not recovered — so an outage is observed as a value that stays above zero, not as a log
/// level.
/// </summary>
/// <remarks>
/// <para>
/// <b>Instruments.</b> <c>microkit.outbox.messages.settled</c> counts the dispositions a batch wrote,
/// tagged <c>outcome</c> (<c>published</c>, <c>retry</c>, <c>dead_letter</c>, <c>deferred</c>), at least
/// once: a partial settlement (event 1007) counts its whole batch, since its written rows never come back;
/// a row whose lease was lost is counted under this processor's verdict and again under the verdict of
/// whoever settles it, which may differ — <c>dead_letter</c> included. A settlement that wrote nothing, or
/// threw, counts nothing: what it committed is unknown, its rows that come back are counted then, and a
/// commit that landed without the store being able to verify it is not counted. A released row is never
/// counted.
/// <c>microkit.outbox.dispatch.delay</c> is the processor's clock once the dispatch returned,
/// minus the row's <see cref="OutboxMessage.CreatedAtUtc"/>, clamped at zero since another instance's
/// clock stamped the row; it samples hand-offs to the endpoint, so a batch whose settlement fails is
/// re-sent and sampled again — at least once, like the delivery. <c>microkit.outbox.endpoints.backoff</c>
/// and <c>microkit.outbox.tenants.backoff</c> are observable up-down counters — the values add up across
/// stores and endpoints — of the endpoint, and of the tenants on it, that have not recovered: 1 from a down
/// verdict until a success, or until a ceiling plus <c>MaxPollingInterval</c> after its back-off ended with
/// no new attempt. Once a back-off ends the endpoint or tenant is half-open, not recovered, and the next
/// attempt comes within <see cref="OutboxProcessorOptions.MaxPollingInterval"/> plus a batch's own time,
/// so one still failing is blocked again before that bound; one with nothing left to send drops out after it.
/// A tenant also drops out when its entry is pruned, a ceiling after its back-off ended, by the next batch.
/// </para>
/// <para>
/// <b>Tags.</b> <c>store.key</c> (<see cref="MessageStoreKey.Value"/>, absent for a processor built with
/// no store key), <c>endpoint</c> (<c>transport</c> or <c>local</c>) and, on a partitioned store's
/// instance with <c>StoreInstanceOptions.TagInstances</c> on, <c>store.instance</c> (#191 D9). A row whose endpoint cannot be
/// routed is tagged <c>transport</c>, the fallback <c>Down</c> and the deferral tally already use. Never a
/// tenant: its cardinality is unbounded, and tenants show in the back-off counts and in event 1011. The
/// OpenTelemetry messaging semantic conventions have Development status and are not followed for now.
/// </para>
/// <para>
/// <b>The back-off counters read memory only</b>, never the database: an observable callback runs on a
/// collector's thread, in sequence with every other, so a slow one delays every metric. A single store is
/// reported from its first cycle, when its back-off state is created and handed here
/// (<see cref="Track(MessageStoreKey, OutboxEndpointBackoff)"/>); one whose processor never activates
/// reports nothing — event 1400 covers it. Once reported, a single store reports every endpoint, zeros
/// included, so an alert resolves. A partitioned store's instance is handed here when the engine starts
/// draining it (<see cref="Track(StoreInstanceKey, OutboxEndpointBackoff)"/>). With instances not tagged,
/// a configured partitioned store reports every endpoint from the start, summed over its instances and
/// zero while it has none, whether or not a processor of it activates (1409 reports a store its check
/// refused, 1411 and 1414 an instance's loop that cannot activate its processor or prepare its scope, 1415
/// an instance that cannot start). With instances tagged, each instance reports from the start of its
/// draining, and a removed instance's series stops being reported once its loops have ended (#191 D5).
/// OpenTelemetry .NET 1.15.1 or later in the host exports only the attribute sets an observable
/// instrument's callback reported in the current collection, so that series goes stale and an alert on it
/// resolves for want of a sample; an earlier version keeps exporting its last value, and such an alert
/// does not resolve.
/// </para>
/// <para>
/// The meter is constructed directly rather than obtained from an <c>IMeterFactory</c>, for the reasons
/// <see cref="InboxMetrics"/> gives. Registered as a singleton with the first store.
/// </para>
/// </remarks>
internal sealed class OutboxMetrics : IDisposable
{
    private const string TransportTag = "transport";
    private const string LocalTag = "local";

    private static readonly double[] DelayBuckets =
        [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600, 1800, 3600];

    private readonly TimeProvider _timeProvider;
    private readonly Counter<long> _settled;
    private readonly Histogram<double> _dispatchDelay;
    private readonly ConcurrentDictionary<StoreInstanceKey, OutboxEndpointBackoff> _stores = new();
    private readonly TimeSpan _halfOpen;
    private readonly bool _tagInstances;
    private readonly MessageStoreKey[] _partitioned;

    /// <summary>Initializes a new <see cref="OutboxMetrics"/>.</summary>
    /// <param name="timeProvider">The clock the back-off counters compare each back-off's end with.</param>
    /// <param name="options">
    /// The outbox options: a back-off counts until <see cref="OutboxProcessorOptions.TransportUnavailableBackoff"/>
    /// plus <see cref="OutboxProcessorOptions.MaxPollingInterval"/> past its end, with no new attempt.
    /// </param>
    /// <param name="instanceOptions">Whether instances are tagged (#191); not tagged when null.</param>
    /// <param name="instances">The partitioned stores, each reported at zero while untagged and empty; none when null.</param>
    public OutboxMetrics(
        TimeProvider timeProvider,
        OutboxProcessorOptions options,
        StoreInstanceOptions? instanceOptions = null,
        StoreInstanceRegistry? instances = null)
    {
        _timeProvider = timeProvider;
        _tagInstances = instanceOptions?.TagInstances ?? false;
        _partitioned = instances?.PartitionedStores ?? [];
        _halfOpen = options.TransportUnavailableBackoff + options.MaxPollingInterval;
        Meter = new Meter(MessagingDiagnostics.OutboxMeterName);

        _settled = Meter.CreateCounter<long>(
            "microkit.outbox.messages.settled",
            unit: "{message}",
            description: "Outbox rows settled by a batch, by outcome. Alert on a dead_letter rate above zero.");

        // Explicit boundaries in seconds, from 10 ms to an hour: the collector defaults are made for
        // milliseconds and would put every sub-second delay in one bucket.
        _dispatchDelay = Meter.CreateHistogram<double>(
            "microkit.outbox.dispatch.delay",
            unit: "s",
            description: "From the row's staging to its endpoint taking it. Alert on a percentile.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DelayBuckets });

        Meter.CreateObservableUpDownCounter<int>(
            "microkit.outbox.endpoints.backoff",
            ObserveEndpoints,
            unit: "{endpoint}",
            description: "1 from the endpoint's down verdict until it recovers. Alert when above zero for a sustained period.");

        Meter.CreateObservableUpDownCounter<int>(
            "microkit.outbox.tenants.backoff",
            ObserveTenants,
            unit: "{tenant}",
            description: "Tenants not recovered on the endpoint. Alert when above zero for a sustained period.");
    }

    /// <summary>Gets the meter, for a test to listen to its own instance.</summary>
    internal Meter Meter { get; }

    /// <summary>Gets a value indicating whether anything listens to the dispatch delay.</summary>
    public bool DispatchDelayEnabled => _dispatchDelay.Enabled;

    /// <summary>The <c>endpoint</c> tag of a dispatcher key: anything but the local endpoint is the transport.</summary>
    /// <param name="endpointKey">A dispatcher key, or <see langword="null"/> for an unroutable row.</param>
    /// <returns>The tag value.</returns>
    public static string EndpointTag(string? endpointKey)
        => endpointKey is OutboxDispatchers.Local ? LocalTag : TransportTag;

    /// <summary>Reports the back-off of <paramref name="store"/> from now on.</summary>
    /// <param name="store">The store.</param>
    /// <param name="backoff">Its endpoint back-off, the store's singleton.</param>
    public void Track(MessageStoreKey store, OutboxEndpointBackoff backoff) => _stores[new StoreInstanceKey(store, null)] = backoff;

    /// <summary>Reports the back-off of an instance of a partitioned store from now on (#191).</summary>
    /// <param name="key">The instance; never a single store's key.</param>
    /// <param name="backoff">Its endpoint back-off.</param>
    public void Track(StoreInstanceKey key, OutboxEndpointBackoff backoff)
    {
        ArgumentNullException.ThrowIfNull(key.Instance);
        _stores[key] = backoff;
    }

    /// <summary>Stops reporting an instance once its loops have ended (#191): its series no longer report.</summary>
    /// <param name="key">The instance.</param>
    public void Untrack(StoreInstanceKey key) => _stores.TryRemove(key, out _);

    /// <summary>Records one dispatch's delay, clamped at zero.</summary>
    /// <param name="delay">The processor's clock once the dispatch returned, minus the row's staging time.</param>
    /// <param name="store">The <c>store.key</c> tag; <see langword="null"/> for none.</param>
    /// <param name="endpointKey">The row's dispatcher key.</param>
    /// <param name="instance">The <c>store.instance</c> tag; <see langword="null"/> for none (#191).</param>
    /// <remarks>
    /// A listener's fault is swallowed: a measurement callback runs on this thread, and the row was
    /// handed over already — counted a failure, it would be retried, charged and re-sent.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A metrics listener's fault must not change a row's verdict.")]
    public void RecordDispatchDelay(TimeSpan delay, string? store, string? endpointKey, string? instance = null)
    {
        try
        {
            var tags = Tags(store, EndpointTag(endpointKey), instance);
            _dispatchDelay.Record(delay > TimeSpan.Zero ? delay.TotalSeconds : 0d, in tags);
        }
        catch (Exception)
        {
            // Deliberately swallowed: see the remarks.
        }
    }

    /// <summary>
    /// Records a batch's whole tally once its settlement wrote at least one row, a partial settlement
    /// (1007) included — at least once: one measurement per non-zero (endpoint, outcome).
    /// </summary>
    /// <param name="store">The <c>store.key</c> tag; <see langword="null"/> for none.</param>
    /// <param name="tally">The batch's tally.</param>
    /// <param name="instance">The <c>store.instance</c> tag; <see langword="null"/> for none (#191).</param>
    /// <remarks>
    /// A listener's fault is swallowed: the batch is settled already, and what follows — events 1010 and
    /// 1011, a configuration fault's rethrow — must still run.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A metrics listener's fault must not undo what follows a settlement.")]
    public void RecordSettled(string? store, in SettledTally tally, string? instance = null)
    {
        try
        {
            for (var cell = 0; cell < SettledTally.Cells; cell++)
            {
                var count = tally[cell];
                if (count == 0)
                {
                    continue;
                }

                var tags = Tags(store, cell < SettledTally.Outcomes ? TransportTag : LocalTag, instance);
                tags.Add("outcome", SettledTally.OutcomeTag(cell % SettledTally.Outcomes));
                _settled.Add(count, in tags);
            }
        }
        catch (Exception)
        {
            // Deliberately swallowed: see the remarks.
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Meter.Dispose();

    private static TagList Tags(string? store, string endpoint, string? instance)
    {
        var tags = default(TagList);
        if (store is not null)
        {
            tags.Add("store.key", store);
        }

        if (instance is not null)
        {
            tags.Add("store.instance", instance);
        }

        tags.Add("endpoint", endpoint);
        return tags;
    }

    private static Measurement<int> Measure(int value, MessageStoreKey store, string endpoint)
        => new(value, new KeyValuePair<string, object?>("store.key", store.Value), new("endpoint", endpoint));

    private static Measurement<int> Measure(int value, StoreInstanceKey key, string endpoint)
        => new(
            value,
            new KeyValuePair<string, object?>("store.key", key.Store.Value),
            new("store.instance", key.Instance),
            new("endpoint", endpoint));

    private List<Measurement<int>> ObserveEndpoints()
        => Observe(static (backoff, endpoint, now, halfOpen) => backoff.IsUnrecoveredAt(endpoint, now, halfOpen) ? 1 : 0);

    private List<Measurement<int>> ObserveTenants()
        => Observe(static (backoff, endpoint, now, halfOpen) => backoff.TenantsOf(endpoint).CountUnrecoveredAt(now, halfOpen));

    /// <summary>
    /// One measurement per single store and endpoint; per instance and endpoint when instances are
    /// tagged; otherwise one per partitioned store and endpoint, summed over its instances — zero while
    /// it has none, so an alert resolves (#191 D9).
    /// </summary>
    private List<Measurement<int>> Observe(Func<OutboxEndpointBackoff, string, DateTimeOffset, TimeSpan, int> value)
    {
        var now = _timeProvider.GetUtcNow();
        var measurements = new List<Measurement<int>>();
        Dictionary<MessageStoreKey, (int Transport, int Local)>? sums = null;
        if (!_tagInstances && _partitioned.Length > 0)
        {
            sums = new Dictionary<MessageStoreKey, (int Transport, int Local)>(_partitioned.Length);
            foreach (var store in _partitioned)
            {
                sums[store] = default;
            }
        }

        foreach (var (key, backoff) in _stores)
        {
            var transport = value(backoff, OutboxDispatchers.Transport, now, _halfOpen);
            var local = value(backoff, OutboxDispatchers.Local, now, _halfOpen);
            if (key.Instance is null)
            {
                measurements.Add(Measure(transport, key.Store, TransportTag));
                measurements.Add(Measure(local, key.Store, LocalTag));
            }
            else if (sums is null)
            {
                measurements.Add(Measure(transport, key, TransportTag));
                measurements.Add(Measure(local, key, LocalTag));
            }
            else
            {
                var sum = sums.GetValueOrDefault(key.Store);
                sums[key.Store] = (sum.Transport + transport, sum.Local + local);
            }
        }

        if (sums is not null)
        {
            foreach (var (store, sum) in sums)
            {
                measurements.Add(Measure(sum.Transport, store, TransportTag));
                measurements.Add(Measure(sum.Local, store, LocalTag));
            }
        }

        return measurements;
    }

    /// <summary>
    /// What one batch settled, per endpoint and outcome, counted in the drain loop where the row's
    /// endpoint is known: integer increments, no allocation, no second pass over the outcomes.
    /// </summary>
    internal struct SettledTally
    {
        /// <summary>The outcomes counted, per endpoint.</summary>
        internal const int Outcomes = 4;

        /// <summary>The cells: the transport's outcomes, then the local endpoint's.</summary>
        internal const int Cells = 2 * Outcomes;

        private CellArray _cells;

        /// <summary>Gets the count of one cell.</summary>
        /// <param name="cell">The cell, below <see cref="Cells"/>.</param>
        public readonly int this[int cell] => _cells[cell];

        /// <summary>The <c>outcome</c> tag of an outcome index.</summary>
        /// <param name="outcome">The index, below <see cref="Outcomes"/>.</param>
        /// <returns>The tag value.</returns>
        public static string OutcomeTag(int outcome) => outcome switch
        {
            0 => "published",
            1 => "retry",
            2 => "dead_letter",
            _ => "deferred",
        };

        /// <summary>Counts one outcome of a row of <paramref name="endpointKey"/>; a released row counts nothing.</summary>
        /// <param name="endpointKey">The row's dispatcher key, or <see langword="null"/> for an unroutable row.</param>
        /// <param name="kind">The outcome.</param>
        public void Add(string? endpointKey, OutboxOutcomeKind kind)
        {
            var outcome = kind switch
            {
                OutboxOutcomeKind.Published => 0,
                OutboxOutcomeKind.Retry => 1,
                OutboxOutcomeKind.DeadLetter => 2,
                OutboxOutcomeKind.Deferred => 3,
                _ => -1,
            };

            if (outcome < 0)
            {
                return;
            }

            _cells[(endpointKey is OutboxDispatchers.Local ? Outcomes : 0) + outcome]++;
        }

        [InlineArray(Cells)]
        private struct CellArray
        {
            private int _element;
        }
    }
}
