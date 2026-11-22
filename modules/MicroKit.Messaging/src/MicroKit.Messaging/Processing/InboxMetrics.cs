using System.Diagnostics.Metrics;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// Ingestion counters, so a deduplicated write is observable as a rate rather than only as a
/// log line, and the tenants not recovered on each store's inbox (#126).
/// </summary>
/// <remarks>
/// <para>
/// The deduplication rate is the metric worth alerting on. A steady low level is healthy — it
/// simply means at-least-once delivery is doing its job. A sustained climb means something
/// upstream is redelivering more than it should: a lease shorter than the real handler
/// duration, a consumer that stalls, a broker replaying. All of those are visible here well
/// before they are visible anywhere else.
/// </para>
/// <para>
/// <b>The meter is constructed directly rather than obtained from an <c>IMeterFactory</c>.</b>
/// <c>System.Diagnostics.Metrics</c> is in the BCL, so this costs no package reference and,
/// more importantly, no DI requirement: an <c>IMeterFactory</c> dependency would oblige every
/// host — including bare <c>ServiceCollection</c> test hosts — to call <c>AddMetrics()</c> or
/// fail at resolution. The only thing a factory buys is per-container meter isolation, which
/// nothing in this library needs. Subscribing is unaffected: OpenTelemetry picks this up with
/// <c>AddMeter(MessagingDiagnostics.InboxMeterName)</c> exactly as it would a factory-created meter.
/// </para>
/// <para>
/// If per-container isolation is ever required, switching to <c>IMeterFactory</c> is a one-line
/// change here plus <c>services.AddMetrics()</c> in the consumer's composition root.
/// </para>
/// <para>
/// <b>The tenants not recovered</b> — <c>microkit.inbox.tenants.backoff</c>, tag <c>store.key</c>, and
/// <c>store.instance</c> on a partitioned store's instance with <c>StoreInstanceOptions.TagInstances</c>
/// on (#191 D9) — are an
/// observable up-down counter read from <see cref="InboxTenantBackoff"/>'s memory, never the database: a
/// tenant counts 1 from a down verdict until a success, or until a ceiling plus <c>MaxPollingInterval</c>
/// (<see cref="InboxProcessorOptions.DependencyUnavailableBackoff"/> plus
/// <see cref="InboxProcessorOptions.MaxPollingInterval"/>) after its back-off ended with no new attempt; it
/// also drops out when its entry is pruned, a ceiling after its back-off ended, by the next retry batch.
/// The bound assumes the retry loop makes the next attempt: a transport delivery that leaves its tenant in
/// back-off with no row — the tenant fails the probe, its deferred row cannot be written, the store is
/// unreachable while writing it, or the host stops while writing it — is next attempted by the
/// transport's redelivery, and a provider pausing longer than the bound reads 0 before it. A single store
/// is reported once its state is created, by the first <see cref="InboxTenantBackoff.For"/> call for it,
/// which a local delivery never makes: its retry loop's first cycle; a dependency outage, or the store
/// unreachable, met by one of its transport deliveries; or, while
/// <see cref="InboxTenantBackoff.IsQuiet"/> is false — a tenant in back-off, a probe pending or a
/// suspicion standing, on any store's inbox — one of its transport deliveries with a tenant that passed
/// the dedup read, or a transport success. So it can read 0 with no outage of its own. A single store none
/// of these reaches reports nothing (event 1400 covers a retry loop whose processor never activates); once
/// reported, it reports zero included, so an alert resolves. A partitioned store's instance state is
/// created when the engine starts draining it (<c>InboxTenantBackoff.Add</c>). With instances not tagged, a
/// configured partitioned store reports from the start, summed over its instances and zero while it has
/// none, whether or not a processor of it activates (1409 reports a store its check refused, 1411 and 1414
/// an instance's loop that cannot activate its processor or prepare its scope, 1415 an instance that
/// cannot start). With instances tagged, each instance reports from the start of its draining, and a
/// removed instance's series stops being reported once its loops have ended and its tenant back-off is
/// released (#191 D5). OpenTelemetry .NET 1.15.1 or later in the host exports only the attribute sets an
/// observable instrument's callback reported in the current collection, so that series goes stale and an
/// alert on it resolves for want of a sample; an earlier version keeps exporting its last value, and such
/// an alert does not resolve.
/// </para>
/// <para>
/// Registered as a singleton by <c>AddMicroKitMessaging()</c>.
/// </para>
/// </remarks>
internal sealed class InboxMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _added;
    private readonly Counter<long> _deduplicated;
    private readonly Counter<long> _unconsumed;
    private readonly InboxTenantBackoff? _tenantBackoff;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _halfOpen;
    private readonly bool _tagInstances;
    private readonly MessageStoreKey[] _partitioned;

    /// <summary>Initializes a new <see cref="InboxMetrics"/>.</summary>
    /// <param name="tenantBackoff">
    /// The stores' tenant back-off the back-off counter reads. Null only for an instance built by hand,
    /// which then reports no store.
    /// </param>
    /// <param name="timeProvider">The clock each back-off's end is compared with; the system clock when null.</param>
    /// <param name="options">
    /// The inbox options, for how long past its end a back-off still counts; the defaults when null.
    /// </param>
    /// <param name="instanceOptions">Whether instances are tagged (#191); not tagged when null.</param>
    /// <param name="instances">The partitioned stores, each reported at zero while untagged and empty; none when null.</param>
    public InboxMetrics(
        InboxTenantBackoff? tenantBackoff = null,
        TimeProvider? timeProvider = null,
        InboxProcessorOptions? options = null,
        StoreInstanceOptions? instanceOptions = null,
        StoreInstanceRegistry? instances = null)
    {
        _tenantBackoff = tenantBackoff;
        _tagInstances = instanceOptions?.TagInstances ?? false;
        _partitioned = instances?.PartitionedStores ?? [];
        _timeProvider = timeProvider ?? TimeProvider.System;
        options ??= new InboxProcessorOptions();
        _halfOpen = options.DependencyUnavailableBackoff + options.MaxPollingInterval;
        _meter = new Meter(MessagingDiagnostics.InboxMeterName);

        _added = _meter.CreateCounter<long>(
            "microkit.inbox.messages.added",
            unit: "{message}",
            description: "Inbox rows inserted — first delivery for a message and consumer.");

        _deduplicated = _meter.CreateCounter<long>(
            "microkit.inbox.messages.deduplicated",
            unit: "{message}",
            description: "Redeliveries the dedup gate absorbed. Normal; watch the rate, not the value.");

        _unconsumed = _meter.CreateCounter<long>(
            "microkit.inbox.envelopes.unconsumed",
            unit: "{message}",
            description:
                "Envelopes whose contract this process understands but no handler consumes. " +
                "Zero rows written; alert on any sustained non-zero value.");

        _meter.CreateObservableUpDownCounter<int>(
            "microkit.inbox.tenants.backoff",
            ObserveTenants,
            unit: "{tenant}",
            description: "Tenants not recovered on the store's inbox. Alert when above zero for a sustained period.");
    }

    /// <summary>Gets the meter, for a test to listen to its own instance.</summary>
    internal Meter Meter => _meter;

    /// <summary>Counts a new delivery whose handler wrote its inbox row, Processed or Failed.</summary>
    /// <param name="consumerType">The handler's consumer type.</param>
    public void RecordAdded(string consumerType)
        => _added.Add(1, new KeyValuePair<string, object?>("consumer.type", consumerType));

    /// <summary>
    /// Counts a delivery the guard absorbed: the handler's row was already there, or a concurrent
    /// delivery committed it first.
    /// </summary>
    /// <param name="consumerType">The handler's consumer type.</param>
    public void RecordDeduplicated(string consumerType)
        => _deduplicated.Add(1, new KeyValuePair<string, object?>("consumer.type", consumerType));

    /// <summary>
    /// Records an envelope that resolved to a local type no handler consumes, so no row was
    /// written for it.
    /// </summary>
    /// <param name="contractName">The wire contract name, recorded as a tag.</param>
    /// <remarks>
    /// Separate from <see cref="RecordAdded"/> and <see cref="RecordDeduplicated"/> because the two answer different questions and the
    /// existing counters cannot express this one: they are tagged by consumer, and here there is no
    /// consumer to name. Tagged by contract name instead, which is what an operator needs to find
    /// the missing registration.
    /// <para>
    /// Zero rows is correct behaviour for a contract nothing here consumes, so this is not an error
    /// counter — but it is indistinguishable from a healthy delivery without it, and that is the
    /// silent success this module treats as blocking.
    /// </para>
    /// </remarks>
    public void RecordUnconsumed(string contractName)
        => _unconsumed.Add(1, new KeyValuePair<string, object?>("contract.name", contractName));

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();

    private List<Measurement<int>> ObserveTenants()
    {
        var measurements = new List<Measurement<int>>();
        if (_tenantBackoff is null)
        {
            return measurements;
        }

        // One measurement per single store; per instance when instances are tagged; otherwise one per
        // partitioned store, summed over its instances — zero while it has none (#191 D9).
        var now = _timeProvider.GetUtcNow();
        Dictionary<MessageStoreKey, int>? sums = null;
        if (!_tagInstances && _partitioned.Length > 0)
        {
            sums = new Dictionary<MessageStoreKey, int>(_partitioned.Length);
            foreach (var store in _partitioned)
            {
                sums[store] = 0;
            }
        }

        foreach (var (key, backoff) in _tenantBackoff.Stores)
        {
            var count = backoff.CountUnrecoveredAt(now, _halfOpen);
            if (key.Instance is null)
            {
                measurements.Add(new Measurement<int>(count, new KeyValuePair<string, object?>("store.key", key.Store.Value)));
            }
            else if (sums is null)
            {
                measurements.Add(new Measurement<int>(
                    count,
                    new KeyValuePair<string, object?>("store.key", key.Store.Value),
                    new KeyValuePair<string, object?>("store.instance", key.Instance)));
            }
            else
            {
                sums[key.Store] = sums.GetValueOrDefault(key.Store) + count;
            }
        }

        if (sums is not null)
        {
            foreach (var (store, sum) in sums)
            {
                measurements.Add(new Measurement<int>(sum, new KeyValuePair<string, object?>("store.key", store.Value)));
            }
        }

        return measurements;
    }
}
