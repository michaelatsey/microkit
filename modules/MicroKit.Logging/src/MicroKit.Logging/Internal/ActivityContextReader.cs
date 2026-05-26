using System.Diagnostics;

namespace MicroKit.Logging.Internal;

internal static class ActivityContextReader
{
    /// <summary>
    /// Reads W3C TraceContext identifiers from the current <see cref="Activity"/>.
    /// Returns (null, null) when no Activity is active.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a <b>point-in-time snapshot</b> taken at scope creation. If the OTEL SDK or
    /// framework creates a child <see cref="Activity"/> after the scope opens (e.g. database call
    /// instrumentation), <see cref="Activity.Current"/> will advance to the child span and these
    /// snapshot IDs will become stale relative to the live trace context.
    /// </para>
    /// <para>
    /// For structured logging sinks without Activity awareness (Seq, Elastic), the snapshot is
    /// the only source of trace context — it should be kept. For the OTEL export path,
    /// <c>MicroKit.Logging.OpenTelemetry.MicroKitLogProcessor</c> scrubs these snapshot
    /// attributes when <see cref="Activity.Current"/> is non-null, relying instead on the live
    /// values the OTEL SDK reads from <see cref="Activity.Current"/> at export time.
    /// </para>
    /// </remarks>
    internal static (string? TraceId, string? SpanId) Read()
    {
        var activity = Activity.Current;
        if (activity is null)
            return (null, null);

        return (activity.TraceId.ToHexString(), activity.SpanId.ToHexString());
    }
}
