using Microsoft.Extensions.Logging;
using MicroKit.Logging.OpenTelemetry;
using NSubstitute;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace MicroKit.Logging.UnitTests.OpenTelemetry;

[Collection("DiagnosticListener")]
public sealed class MicroKitLogProcessorTests : IDisposable
{
    private readonly List<IReadOnlyList<KeyValuePair<string, object?>>?> _exported = [];
    private readonly ILogContextAccessor _accessor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public MicroKitLogProcessorTests()
    {
        _accessor = Substitute.For<ILogContextAccessor>();
        var processor = new MicroKitLogProcessor(_accessor);
        var exporter = new CapturingExporter(_exported);

        _loggerFactory = LoggerFactory.Create(b =>
            b.AddOpenTelemetry(o =>
            {
                o.AddProcessor(processor);
                o.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
            }));

        _logger = _loggerFactory.CreateLogger(nameof(MicroKitLogProcessorTests));
    }

    [Fact]
    public void OnEnd_WhenContextIsNull_DoesNotAddAnyAttributes()
    {
        _accessor.Current.Returns((IOperationContext?)null);

        _logger.LogInformation("test message");

        _exported.Should().ContainSingle();
        var attrs = _exported[0];
        attrs.Should().NotContain(kv => kv.Key == LogPropertyNames.CorrelationId);
        attrs.Should().NotContain(kv => kv.Key == LogPropertyNames.OperationId);
        attrs.Should().NotContain(kv => kv.Key == LogPropertyNames.TenantId);
        attrs.Should().NotContain(kv => kv.Key == LogPropertyNames.UserId);
    }

    [Fact]
    public void OnEnd_WhenContextHasCorrelationId_AddsCorrelationIdAttribute()
    {
        var context = BuildContext(correlationId: "ctx-123");
        _accessor.Current.Returns(context);

        _logger.LogInformation("test message");

        _exported.Should().ContainSingle();
        _exported[0].Should().Contain(kv => kv.Key == LogPropertyNames.CorrelationId && kv.Value!.ToString() == "ctx-123");
    }

    [Fact]
    public void OnEnd_WhenContextHasAllProperties_AddsAllFourAttributes()
    {
        var context = BuildContext(
            correlationId: "corr-1",
            operationId: "op-1",
            tenantId: "tenant-1",
            userId: "user-1");
        _accessor.Current.Returns(context);

        _logger.LogInformation("test message");

        _exported.Should().ContainSingle();
        var attrs = _exported[0]!;
        attrs.Should().Contain(kv => kv.Key == LogPropertyNames.CorrelationId && kv.Value!.ToString() == "corr-1");
        attrs.Should().Contain(kv => kv.Key == LogPropertyNames.OperationId && kv.Value!.ToString() == "op-1");
        attrs.Should().Contain(kv => kv.Key == LogPropertyNames.TenantId && kv.Value!.ToString() == "tenant-1");
        attrs.Should().Contain(kv => kv.Key == LogPropertyNames.UserId && kv.Value!.ToString() == "user-1");
    }

    [Fact]
    public void OnEnd_WhenAttributeAlreadyPresent_DoesNotOverride()
    {
        var context = BuildContext(correlationId: "from-context");
        _accessor.Current.Returns(context);

        // Structured log param sets CorrelationId explicitly — processor must not override it
        _logger.LogInformation("test {CorrelationId}", "from-log-statement");

        _exported.Should().ContainSingle();
        var correlationAttr = _exported[0]!
            .Where(kv => kv.Key == LogPropertyNames.CorrelationId)
            .ToList();

        correlationAttr.Should().ContainSingle("duplicate attribute must not be added");
        correlationAttr[0].Value.Should().Be("from-log-statement");
    }

    [Fact]
    public void OnEnd_TraceIdAndSpanId_AreNeverAdded()
    {
        var context = BuildContext(correlationId: "corr-x");
        _accessor.Current.Returns(context);

        _logger.LogInformation("test message");

        _exported.Should().ContainSingle();
        var attrs = _exported[0];
        attrs.Should().NotContain(kv => kv.Key == LogPropertyNames.TraceId);
        attrs.Should().NotContain(kv => kv.Key == LogPropertyNames.SpanId);
    }

    [Fact]
    public void OnEnd_WhenContextHasProperties_AndNoExistingAttributes_SetsAttributesDirectly()
    {
        // Plain message (no structured parameters) → data.Attributes may be null or empty.
        // Verifies the else branch where existing attributes are absent.
        var context = BuildContext(correlationId: "bare-log");
        _accessor.Current.Returns(context);

        // Log with no structured parameters — no pre-existing attributes on the record
        _logger.Log(LogLevel.Warning, "bare log with no params");

        _exported.Should().ContainSingle();
        var attrs = _exported[0];
        attrs.Should().NotBeNull();
        attrs.Should().Contain(kv => kv.Key == LogPropertyNames.CorrelationId && kv.Value!.ToString() == "bare-log");
    }

    [Fact]
    public void OnEnd_WhenActivityCurrentIsNonNull_ScrubsSnapshotTraceAndSpanFromAttributes()
    {
        // Simulate stale snapshot TraceId/SpanId in MEL scope state by using a structured
        // log parameter with those property names. MicroKitLogProcessor must scrub them when
        // Activity.Current is non-null (the OTEL SDK will supply live values from the Activity).
        using var source = new ActivitySource("MicroKit.UnitTest.Scrub");
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(source.Name)
            .Build();

        var context = BuildContext(correlationId: "scrub-test");
        _accessor.Current.Returns(context);

        using (source.StartActivity("scrub-span"))
        {
            // Inject stale TraceId/SpanId as structured log params (mimics MEL scope state snapshot)
            _logger.LogInformation("msg {TraceId} {SpanId}", "stale-trace", "stale-span");
        }

        _exported.Should().ContainSingle();
        var attrs = _exported[0];
        attrs.Should().NotContain(kv => kv.Key == LogPropertyNames.TraceId,
            "stale TraceId snapshot must be scrubbed when Activity.Current is live");
        attrs.Should().NotContain(kv => kv.Key == LogPropertyNames.SpanId,
            "stale SpanId snapshot must be scrubbed when Activity.Current is live");
        // CorrelationId from context must still be present
        attrs.Should().Contain(kv => kv.Key == LogPropertyNames.CorrelationId);
    }

    public void Dispose() => _loggerFactory.Dispose();

    private static IOperationContext BuildContext(
        string correlationId = "default-id",
        string? operationId = null,
        string? tenantId = null,
        string? userId = null)
    {
        var ctx = Substitute.For<IOperationContext>();
        ctx.CorrelationId.Returns(correlationId);
        ctx.OperationId.Returns(operationId);
        ctx.TenantId.Returns(tenantId);
        ctx.UserId.Returns(userId);
        ctx.TraceId.Returns((string?)null);
        ctx.SpanId.Returns((string?)null);
        ctx.RequestId.Returns((string?)null);
        return ctx;
    }

    private sealed class CapturingExporter(List<IReadOnlyList<KeyValuePair<string, object?>>?> target)
        : BaseExporter<LogRecord>
    {
        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            foreach (var record in batch)
                target.Add(record.Attributes);
            return ExportResult.Success;
        }
    }
}
