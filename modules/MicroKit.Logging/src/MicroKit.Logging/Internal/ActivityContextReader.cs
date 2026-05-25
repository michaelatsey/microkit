using System.Diagnostics;

namespace MicroKit.Logging.Internal;

internal static class ActivityContextReader
{
    /// <summary>
    /// Reads W3C TraceContext identifiers from the current <see cref="Activity"/>.
    /// Returns (null, null) when no Activity is active.
    /// </summary>
    internal static (string? TraceId, string? SpanId) Read()
    {
        var activity = Activity.Current;
        if (activity is null)
            return (null, null);

        return (activity.TraceId.ToHexString(), activity.SpanId.ToHexString());
    }
}
