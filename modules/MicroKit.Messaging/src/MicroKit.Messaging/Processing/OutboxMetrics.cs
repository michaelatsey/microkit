using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The outbox's instruments (#126), on the meter named by <see cref="MessagingDiagnostics.OutboxMeterName"/>:
/// what each batch settled, how long a row waited before its endpoint took it, and what is held in
/// back-off right now — so an outage is observed as a value that stays above zero, not as a log level.
/// </summary>
/// <remarks>
/// <para>
/// <b>Instruments.</b> <c>microkit.outbox.messages.settled</c> counts the dispositions a batch wrote,
/// tagged <c>outcome</c> (<c>published</c>, <c>retry</c>, <c>dead_letter</c>, <c>deferred</c>): it is
/// recorded once the settlement wrote every outcome, the condition of event 1010. A settlement that
/// fails counts nothing, and its rows are counted when they come back. A partial one (event 1007) counts
/// nothing either: the store returns how many rows it wrote, not which, so any count would be wrong by the
/// rows written or by the rows lost. A released row is never counted. <c>microkit.outbox.dispatch.delay</c> is the processor's clock once the dispatch returned,
/// minus the row's <see cref="OutboxMessage.CreatedAtUtc"/>, clamped at zero since another instance's
/// clock stamped the row; it samples hand-offs to the endpoint, so a batch whose settlement fails is
/// re-sent and sampled again — at least once, like the delivery. <c>microkit.outbox.endpoints.backoff</c>
/// (1 while the endpoint is in back-off) and <c>microkit.outbox.tenants.backoff</c> (the tenants whose
/// back-off is in force on it) are observable up-down counters: the values add up across stores and
/// endpoints.
/// </para>
/// <para>
/// <b>Tags.</b> <c>store.key</c> (<see cref="MessageStoreKey.Value"/>, absent for a processor built with
/// no store key) and <c>endpoint</c> (<c>transport</c> or <c>local</c>). A row whose endpoint cannot be
/// routed is tagged <c>transport</c>, the fallback <c>Down</c> and the deferral tally already use. Never a
/// tenant: its cardinality is unbounded, and tenants show in the back-off counts and in event 1011. The
/// OpenTelemetry messaging semantic conventions have Development status and are not followed for now.
/// </para>
/// <para>
/// <b>The back-off counters read memory only</b>, never the database: an observable callback runs on a
/// collector's thread, in sequence with every other, so a slow one delays every metric. A store is
/// reported from its first cycle, when its back-off state is created and handed here
/// (<see cref="Track"/>); a store whose processor never activates reports nothing — event 1400 covers it.
/// Each known store reports every endpoint, zeros included, so an alert resolves. A back-off counts
/// while it is in force: between its end and the next attempt — up to the loop's
/// <see cref="OutboxProcessorOptions.MaxPollingInterval"/> when nothing else is claimable — a down
/// endpoint reads 0 until that attempt fails again, so an alert takes the maximum over a window longer
/// than that interval.
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
    private readonly ConcurrentDictionary<MessageStoreKey, OutboxEndpointBackoff> _stores = new();

    /// <summary>Initializes a new <see cref="OutboxMetrics"/>.</summary>
    /// <param name="timeProvider">The clock the back-off counters compare each back-off's end with.</param>
    public OutboxMetrics(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
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
            description: "1 while the endpoint is in back-off. Alert when above zero for a sustained period.");

        Meter.CreateObservableUpDownCounter<int>(
            "microkit.outbox.tenants.backoff",
            ObserveTenants,
            unit: "{tenant}",
            description: "Tenants in back-off on the endpoint. Alert when above zero for a sustained period.");
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
    public void Track(MessageStoreKey store, OutboxEndpointBackoff backoff) => _stores[store] = backoff;

    /// <summary>Records one dispatch's delay, clamped at zero.</summary>
    /// <param name="delay">The processor's clock once the dispatch returned, minus the row's staging time.</param>
    /// <param name="store">The <c>store.key</c> tag; <see langword="null"/> for none.</param>
    /// <param name="endpointKey">The row's dispatcher key.</param>
    /// <remarks>
    /// A listener's fault is swallowed: a measurement callback runs on this thread, and the row was
    /// handed over already — counted a failure, it would be retried, charged and re-sent.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A metrics listener's fault must not change a row's verdict.")]
    public void RecordDispatchDelay(TimeSpan delay, string? store, string? endpointKey)
    {
        try
        {
            var tags = Tags(store, EndpointTag(endpointKey));
            _dispatchDelay.Record(delay > TimeSpan.Zero ? delay.TotalSeconds : 0d, in tags);
        }
        catch (Exception)
        {
            // Deliberately swallowed: see the remarks.
        }
    }

    /// <summary>Records what a fully settled batch wrote: one measurement per non-zero (endpoint, outcome).</summary>
    /// <param name="store">The <c>store.key</c> tag; <see langword="null"/> for none.</param>
    /// <param name="tally">The batch's tally.</param>
    /// <remarks>
    /// A listener's fault is swallowed: the batch is settled already, and what follows — events 1010 and
    /// 1011, a configuration fault's rethrow — must still run.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A metrics listener's fault must not undo what follows a settlement.")]
    public void RecordSettled(string? store, in SettledTally tally)
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

                var tags = Tags(store, cell < SettledTally.Outcomes ? TransportTag : LocalTag);
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

    private static TagList Tags(string? store, string endpoint)
    {
        var tags = default(TagList);
        if (store is not null)
        {
            tags.Add("store.key", store);
        }

        tags.Add("endpoint", endpoint);
        return tags;
    }

    private static Measurement<int> Measure(int value, MessageStoreKey store, string endpoint)
        => new(value, new KeyValuePair<string, object?>("store.key", store.Value), new("endpoint", endpoint));

    private List<Measurement<int>> ObserveEndpoints()
    {
        var now = _timeProvider.GetUtcNow();
        var measurements = new List<Measurement<int>>();
        foreach (var (store, backoff) in _stores)
        {
            measurements.Add(Measure(backoff.IsBlocked(OutboxDispatchers.Transport, now, out _) ? 1 : 0, store, TransportTag));
            measurements.Add(Measure(backoff.IsBlocked(OutboxDispatchers.Local, now, out _) ? 1 : 0, store, LocalTag));
        }

        return measurements;
    }

    private List<Measurement<int>> ObserveTenants()
    {
        var now = _timeProvider.GetUtcNow();
        var measurements = new List<Measurement<int>>();
        foreach (var (store, backoff) in _stores)
        {
            measurements.Add(Measure(backoff.TenantsOf(OutboxDispatchers.Transport).CountBlockedAt(now), store, TransportTag));
            measurements.Add(Measure(backoff.TenantsOf(OutboxDispatchers.Local).CountBlockedAt(now), store, LocalTag));
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
