namespace MicroKit.Logging.IntegrationTests.OpenTelemetry;

/// <summary>
/// Integration tests verifying that Activity correlation (TraceId/SpanId) flows correctly
/// through the full MicroKit → OTEL SDK → exporter pipeline.
/// </summary>
[Collection("OtelCorrelation")]
public sealed class ActivityCorrelationIntegrationTests : IDisposable
{
    private static readonly ActivitySource s_testSource = new("MicroKit.IntegrationTest");

    private readonly List<CapturedLogRecord> _exported = [];
    private readonly ServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly ILogScopeFactory _scopeFactory;
    private readonly TracerProvider _tracerProvider;

    public ActivityCorrelationIntegrationTests()
    {
        var services = new ServiceCollection();
        services.AddMicroKitLogging();
        services.AddMicroKitOpenTelemetry();
        services.AddLogging(b => b.AddOpenTelemetry(o =>
            o.AddProcessor(new SimpleLogRecordExportProcessor(new CapturingExporter(_exported)))));

        _services = services.BuildServiceProvider();
        _loggerFactory = _services.GetRequiredService<ILoggerFactory>();
        _logger = _loggerFactory.CreateLogger(nameof(ActivityCorrelationIntegrationTests));
        _scopeFactory = _services.GetRequiredService<ILogScopeFactory>();

        _tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(s_testSource.Name)
            .AddMicroKitLoggingSources()
            .Build();
    }

    [Fact]
    public void Log_OutsideAnySpan_LogRecordHasNoTraceContext()
    {
        _logger.LogInformation("no span message");

        _exported.Count.ShouldBe(1);
        var record = _exported[0];
        record.TraceId.ShouldBe(default(ActivityTraceId));
        record.SpanId.ShouldBe(default(ActivitySpanId));
    }

    [Fact]
    public void Log_InsideActiveSpan_LogRecordTraceContextMatchesSpan()
    {
        ActivityTraceId expectedTraceId;
        ActivitySpanId expectedSpanId;

        using (var span = s_testSource.StartActivity("test-span"))
        {
            span.ShouldNotBeNull();
            expectedTraceId = span!.TraceId;
            expectedSpanId = span.SpanId;

            _logger.LogInformation("inside span");
        }

        _exported.Count.ShouldBe(1);
        var record = _exported[0];
        record.TraceId.ShouldBe(expectedTraceId);
        record.SpanId.ShouldBe(expectedSpanId);
    }

    [Fact]
    public void Log_WithMicroKitScope_CorrelationIdAppearsInAttributes()
    {
        using var scope = _scopeFactory.BeginOperationScope("test-correlation-id");

        _logger.LogInformation("scoped message");

        _exported.Count.ShouldBe(1);
        _exported[0].Attributes.ShouldNotBeNull();
        _exported[0].Attributes!.ShouldContain(kv => kv.Key == LogPropertyNames.CorrelationId
                        && kv.Value!.ToString() == "test-correlation-id");
    }

    [Fact]
    public void Log_InsideChildSpan_LogRecordCarriesChildSpanId_NotParentSnapshot()
    {
        // Scenario: MicroKit scope captures parent span at scope-open, then a child span is started.
        // The exported log record must carry the *child* span's ID (live Activity.Current),
        // not the stale parent snapshot from the MEL scope state.
        using var parentSpan = s_testSource.StartActivity("parent-span");
        parentSpan.ShouldNotBeNull();

        using (var scope = _scopeFactory.BeginOperationScope("corr-child-test"))
        {
            // Start child span — Activity.Current advances past the parent
            using var childSpan = s_testSource.StartActivity("child-span");
            childSpan.ShouldNotBeNull();

            _logger.LogInformation("inside child span");
        }

        _exported.Count.ShouldBe(1);
        var record = _exported[0];

        // LogRecord.SpanId must match the child (not the parent that was snapshotted)
        record.SpanId.ShouldNotBe(parentSpan!.SpanId, "child span must win over stale snapshot");

        // MicroKitLogProcessor scrubs snapshot TraceId/SpanId from attributes when Activity is live
        var attrs = record.Attributes.ShouldNotBeNull();
        attrs.ShouldNotContain(kv => kv.Key == LogPropertyNames.TraceId,
            "stale TraceId snapshot must be scrubbed from attributes");
        attrs.ShouldNotContain(kv => kv.Key == LogPropertyNames.SpanId,
            "stale SpanId snapshot must be scrubbed from attributes");

        // CorrelationId from MicroKit scope must still be present
        attrs.ShouldContain(kv =>
            kv.Key == LogPropertyNames.CorrelationId && kv.Value!.ToString() == "corr-child-test");
    }

    [Fact]
    public async Task Log_AfterAwaitWithinScope_CorrelationIdStillPresent()
    {
        using var scope = _scopeFactory.BeginOperationScope("async-flow-test");

        await Task.Yield(); // force continuation on a different thread-pool thread

        _logger.LogInformation("post-yield message");

        _exported.Count.ShouldBe(1);
        var attrs = _exported[0].Attributes.ShouldNotBeNull();
        attrs.ShouldContain(kv =>
            kv.Key == LogPropertyNames.CorrelationId && kv.Value!.ToString() == "async-flow-test");
    }

    [Fact]
    public void AddMicroKitOpenTelemetry_CalledTwice_ProcessorNotDuplicated()
    {
        var services = new ServiceCollection();
        services.AddMicroKitLogging();
        services.AddMicroKitOpenTelemetry();
        services.AddMicroKitOpenTelemetry(); // second call must be idempotent
        services.AddLogging(b => b.AddOpenTelemetry(o =>
            o.AddProcessor(new SimpleLogRecordExportProcessor(new CapturingExporter(_exported)))));

        using var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("idempotency");
        var scopeFactory = sp.GetRequiredService<ILogScopeFactory>();

        using (var scope = scopeFactory.BeginOperationScope("dup-test"))
            logger.LogInformation("dedup message");

        // Each attribute should appear exactly once — no duplicate enrichment from double registration
        var correlationAttrs = _exported
            .SelectMany(r => r.Attributes ?? [])
            .Where(kv => kv.Key == LogPropertyNames.CorrelationId)
            .ToList();

        correlationAttrs.Count.ShouldBe(1, "double AddMicroKitOpenTelemetry must not duplicate CorrelationId");
    }

    public void Dispose()
    {
        _tracerProvider.Dispose();
        _loggerFactory.Dispose();
        _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed record CapturedLogRecord(
        ActivityTraceId TraceId,
        ActivitySpanId SpanId,
        IReadOnlyList<KeyValuePair<string, object?>>? Attributes);

    private sealed class CapturingExporter(List<CapturedLogRecord> target) : BaseExporter<LogRecord>
    {
        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            foreach (var record in batch)
            {
                // Snapshot immediately — attributes list may be reused after Export returns
                List<KeyValuePair<string, object?>>? attrsCopy = null;
                var attrs = record.Attributes;
                if (attrs is { Count: > 0 })
                {
                    attrsCopy = new List<KeyValuePair<string, object?>>(attrs.Count);
                    for (int i = 0; i < attrs.Count; i++)
                        attrsCopy.Add(attrs[i]);
                }

                target.Add(new CapturedLogRecord(record.TraceId, record.SpanId, attrsCopy));
            }

            return ExportResult.Success;
        }
    }
}
