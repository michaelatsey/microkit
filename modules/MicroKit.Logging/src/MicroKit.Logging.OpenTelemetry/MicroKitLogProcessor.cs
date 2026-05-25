namespace MicroKit.Logging.OpenTelemetry;

/// <summary>
/// OpenTelemetry log processor that enriches <see cref="LogRecord"/> instances with
/// MicroKit ambient context properties (CorrelationId, OperationId, TenantId, UserId).
/// </summary>
/// <remarks>
/// <para>
/// Register via <see cref="LoggingOpenTelemetryExtensions.AddMicroKitOpenTelemetry(IServiceCollection)"/>
/// after <c>AddMicroKitLogging()</c>.
/// </para>
/// <para>
/// TraceId and SpanId are intentionally excluded — the OTEL SDK reads those from
/// <see cref="Activity.Current"/> automatically, avoiding duplicate attributes on export.
/// </para>
/// </remarks>
public sealed class MicroKitLogProcessor : BaseProcessor<LogRecord>
{
    private readonly ILogContextAccessor _accessor;

    /// <summary>
    /// Initializes a new instance of <see cref="MicroKitLogProcessor"/>.
    /// </summary>
    /// <param name="accessor">Provides access to the current MicroKit operation context.</param>
    public MicroKitLogProcessor(ILogContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <inheritdoc/>
    public override void OnEnd(LogRecord data)
    {
        // When a live Activity is present, remove snapshot TraceId/SpanId that may have been
        // pushed into attributes by the MEL scope at scope-open time. The OTEL SDK reads
        // LogRecord.TraceId/SpanId from Activity.Current at export; keeping the snapshot would
        // produce stale, duplicate values when a child span is active.
        if (Activity.Current is not null)
            ScrubActivityIds(data);

        var context = _accessor.Current;
        if (context is null) return;

        var existing = data.Attributes;
        List<KeyValuePair<string, object?>>? additions = null;

        MaybeAdd(existing, LogPropertyNames.CorrelationId, context.CorrelationId, ref additions);
        MaybeAdd(existing, LogPropertyNames.OperationId, context.OperationId, ref additions);
        MaybeAdd(existing, LogPropertyNames.TenantId, context.TenantId, ref additions);
        MaybeAdd(existing, LogPropertyNames.UserId, context.UserId, ref additions);

        if (additions is null) return;

        if (existing is { Count: > 0 })
        {
            var merged = new List<KeyValuePair<string, object?>>(existing.Count + additions.Count);
            for (int i = 0; i < existing.Count; i++)
                merged.Add(existing[i]);
            merged.AddRange(additions);
            data.Attributes = merged;
        }
        else
        {
            data.Attributes = additions;
        }
    }

    private static void ScrubActivityIds(LogRecord data)
    {
        var existing = data.Attributes;
        if (existing is null || existing.Count == 0) return;

        List<KeyValuePair<string, object?>>? filtered = null;
        for (int i = 0; i < existing.Count; i++)
        {
            var key = existing[i].Key;
            bool isActivityId = string.Equals(key, LogPropertyNames.TraceId, StringComparison.Ordinal)
                             || string.Equals(key, LogPropertyNames.SpanId, StringComparison.Ordinal);
            if (isActivityId)
            {
                if (filtered is null)
                {
                    filtered = new List<KeyValuePair<string, object?>>(existing.Count - 1);
                    for (int j = 0; j < i; j++)
                        filtered.Add(existing[j]);
                }
            }
            else if (filtered is not null)
            {
                filtered.Add(existing[i]);
            }
        }

        if (filtered is not null)
            data.Attributes = filtered.Count > 0 ? filtered : null;
    }

    private static void MaybeAdd(
        IReadOnlyList<KeyValuePair<string, object?>>? existing,
        string key,
        string? value,
        ref List<KeyValuePair<string, object?>>? additions)
    {
        if (string.IsNullOrEmpty(value)) return;

        if (existing is not null)
        {
            for (int i = 0; i < existing.Count; i++)
            {
                if (string.Equals(existing[i].Key, key, StringComparison.Ordinal))
                    return;
            }
        }

        additions ??= new List<KeyValuePair<string, object?>>(4);
        additions.Add(new(key, value));
    }
}
