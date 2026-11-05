namespace MicroKit.Messaging;

/// <summary>
/// The names a host needs to subscribe to this module's telemetry.
/// </summary>
/// <remarks>
/// <para>
/// This type exists because the instrumentation itself is an implementation detail while the
/// <i>names</i> it publishes are a compatibility commitment. <c>InboxMetrics</c> owns the
/// <see cref="System.Diagnostics.Metrics.Meter"/> and became internal with #185; a host still has
/// to name that meter to receive anything, so the name lives here instead of on the recorder.
/// </para>
/// <para>
/// Changing a value on this type is a <b>behaviour change</b>, not a rename: a deployed collector
/// configured for the old name silently stops receiving data. Add a member, never repurpose one.
/// </para>
/// <para>
/// It is named for diagnostics rather than metrics because it will also carry the
/// <see cref="System.Diagnostics.ActivitySource"/> name when tracing lands (#186, #126).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithMetrics(m => m.AddMeter(MessagingDiagnostics.InboxMeterName));
/// </code>
/// </example>
public static class MessagingDiagnostics
{
    /// <summary>
    /// The meter carrying the three inbox ingestion counters —
    /// <c>microkit.inbox.messages.added</c>, <c>microkit.inbox.messages.deduplicated</c> and
    /// <c>microkit.inbox.envelopes.unconsumed</c>.
    /// </summary>
    /// <remarks>
    /// The deduplication counter's <b>rate</b> is the signal, not its value: a steady low level is
    /// healthy, a sustained climb means a lease set too short, a stalling consumer, or a broker
    /// replaying. The unconsumed counter separates a correct zero-row delivery from a silent one.
    /// </remarks>
    public const string InboxMeterName = "MicroKit.Messaging.Inbox";
}
