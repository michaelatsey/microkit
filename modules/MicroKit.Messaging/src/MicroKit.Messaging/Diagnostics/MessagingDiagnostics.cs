namespace MicroKit.Messaging;

/// <summary>
/// The names a host needs to subscribe to this module's telemetry.
/// </summary>
/// <remarks>
/// <para>
/// This type exists because the instrumentation itself is an implementation detail while the
/// <i>names</i> it publishes are a compatibility commitment. <c>InboxMetrics</c> and
/// <c>OutboxMetrics</c> own the <see cref="System.Diagnostics.Metrics.Meter"/>s and are internal; a host
/// still has to name each meter to receive anything, so the names live here instead of on the recorders.
/// </para>
/// <para>
/// Changing a value on this type is a <b>behaviour change</b>, not a rename: a deployed collector
/// configured for the old name silently stops receiving data. Add a member, never repurpose one.
/// </para>
/// <para>
/// It is named for diagnostics rather than metrics because it will also carry the
/// <see cref="System.Diagnostics.ActivitySource"/> name when tracing lands.
/// </para>
/// <para>
/// The instruments do not follow the OpenTelemetry messaging semantic conventions, which have
/// Development status. The back-off instruments are up-down counters — their values add up across stores
/// and endpoints — which Prometheus shows as gauges. A back-off counts while it is in force, so a long
/// outage reads 0 between the end of one back-off and the next failed attempt, which waits for the loop's
/// own delay — up to <c>MaxPollingInterval</c>, <c>TransportUnavailableBackoff</c> or
/// <c>DependencyUnavailableBackoff</c>: alert on the maximum over a window longer than the largest.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithMetrics(m => m
///         .AddMeter(MessagingDiagnostics.InboxMeterName)
///         .AddMeter(MessagingDiagnostics.OutboxMeterName));
/// </code>
/// </example>
public static class MessagingDiagnostics
{
    /// <summary>
    /// The meter carrying the three inbox ingestion counters —
    /// <c>microkit.inbox.messages.added</c>, <c>microkit.inbox.messages.deduplicated</c> and
    /// <c>microkit.inbox.envelopes.unconsumed</c> — and the observable up-down counter
    /// <c>microkit.inbox.tenants.backoff</c>.
    /// </summary>
    /// <remarks>
    /// The deduplication counter's <b>rate</b> is the signal, not its value: a steady low level is
    /// healthy, a sustained climb means a lease set too short, a stalling consumer, or a broker
    /// replaying. The unconsumed counter separates a correct zero-row delivery from a silent one.
    /// <c>microkit.inbox.tenants.backoff</c> (tag <c>store.key</c>) is the number of tenants whose
    /// back-off is in force on a store's inbox: above zero for a sustained period, a tenant's dependency
    /// is down.
    /// </remarks>
    public const string InboxMeterName = "MicroKit.Messaging.Inbox";

    /// <summary>
    /// The meter carrying the outbox's instruments — the counter <c>microkit.outbox.messages.settled</c>,
    /// the histogram <c>microkit.outbox.dispatch.delay</c> and the observable up-down counters
    /// <c>microkit.outbox.endpoints.backoff</c> and <c>microkit.outbox.tenants.backoff</c>.
    /// </summary>
    /// <remarks>
    /// Every instrument is tagged <c>store.key</c> and <c>endpoint</c> (<c>transport</c> or
    /// <c>local</c>); never a tenant. <c>messages.settled</c> counts what a batch wrote, tagged
    /// <c>outcome</c> (<c>published</c>, <c>retry</c>, <c>dead_letter</c>, <c>deferred</c>): a
    /// <c>dead_letter</c> rate above zero is lost work for an operator. <c>dispatch.delay</c>, in seconds,
    /// runs from the row's staging to its endpoint taking it: alert on a percentile. The two back-off
    /// counters — 1 while an endpoint is in back-off, and the tenants in back-off on it — observe an outage
    /// as a value: above zero for a sustained period, an endpoint or a tenant is down.
    /// </remarks>
    public const string OutboxMeterName = "MicroKit.Messaging.Outbox";
}
