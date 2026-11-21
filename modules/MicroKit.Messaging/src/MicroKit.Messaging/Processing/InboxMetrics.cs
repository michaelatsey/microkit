using System.Diagnostics.Metrics;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// Ingestion counters, so a deduplicated write is observable as a rate rather than only as a
/// log line.
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
/// Registered as a singleton by <c>AddMicroKitMessaging()</c>.
/// </para>
/// </remarks>
internal sealed class InboxMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _added;
    private readonly Counter<long> _deduplicated;
    private readonly Counter<long> _unconsumed;

    /// <summary>Initializes a new <see cref="InboxMetrics"/>.</summary>
    public InboxMetrics()
    {
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
    }

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
}
