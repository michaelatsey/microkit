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
