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
/// <c>AddMeter(InboxMetrics.MeterName)</c> exactly as it would a factory-created meter.
/// </para>
/// <para>
/// If per-container isolation is ever required, switching to <c>IMeterFactory</c> is a one-line
/// change here plus <c>services.AddMetrics()</c> in the consumer's composition root.
/// </para>
/// <para>
/// Registered as a singleton by <c>AddMicroKitMessaging()</c>.
/// </para>
/// </remarks>
public sealed class InboxMetrics : IDisposable
{
    /// <summary>The meter name to subscribe to.</summary>
    public const string MeterName = "MicroKit.Messaging.Inbox";

    private readonly Meter _meter;
    private readonly Counter<long> _added;
    private readonly Counter<long> _deduplicated;

    /// <summary>Initializes a new <see cref="InboxMetrics"/>.</summary>
    public InboxMetrics()
    {
        _meter = new Meter(MeterName);

        _added = _meter.CreateCounter<long>(
            "microkit.inbox.messages.added",
            unit: "{message}",
            description: "Inbox rows inserted — first delivery for a message and consumer.");

        _deduplicated = _meter.CreateCounter<long>(
            "microkit.inbox.messages.deduplicated",
            unit: "{message}",
            description: "Redeliveries the dedup gate absorbed. Normal; watch the rate, not the value.");
    }

    /// <summary>Records the outcome of one ingestion attempt.</summary>
    /// <param name="result">What the write did.</param>
    /// <param name="consumerType">The consumer the row belongs to, recorded as a tag.</param>
    public void Record(InboxWriteResult result, string consumerType)
    {
        var tag = new KeyValuePair<string, object?>("consumer.type", consumerType);

        switch (result)
        {
            case InboxWriteResult.Added:
                _added.Add(1, tag);
                break;

            case InboxWriteResult.AlreadyPresent:
                _deduplicated.Add(1, tag);
                break;

            default:
                break;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
