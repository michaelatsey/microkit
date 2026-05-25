using Microsoft.Extensions.Logging.Abstractions;
using MicroKit.Logging.Internal;

namespace MicroKit.Logging.UnitTests.Diagnostics;

[Collection("DiagnosticListener")]
public sealed class CorrelationDiagnosticsTests : IDisposable
{
    private readonly DiagnosticListenerSubscriber _subscriber;
    private readonly LogScopeFactory _factory;

    public CorrelationDiagnosticsTests()
    {
        _subscriber = new DiagnosticListenerSubscriber("MicroKit.Logging");
        _factory = BuildFactory();
    }

    [Fact]
    public void BeginOperationScope_WithNoArg_EmitsCorrelationGeneratedEvent()
    {
        using var scope = _factory.BeginOperationScope();

        _subscriber.Events.Should().ContainSingle(e => e.EventName == DiagnosticEventNames.CorrelationGenerated);
        _subscriber.Events.Should().NotContain(e => e.EventName == DiagnosticEventNames.CorrelationResolved);
        var payload = _subscriber.Events.Single(e => e.EventName == DiagnosticEventNames.CorrelationGenerated).Payload;
        DiagnosticListenerSubscriber.GetPayloadValue<string>(payload, "CorrelationId").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void BeginOperationScope_WithProvidedId_EmitsCorrelationResolvedEvent()
    {
        const string id = "inbound-id-abc";

        using var scope = _factory.BeginOperationScope(id);

        _subscriber.Events.Should().ContainSingle(e => e.EventName == DiagnosticEventNames.CorrelationResolved);
        _subscriber.Events.Should().NotContain(e => e.EventName == DiagnosticEventNames.CorrelationGenerated);
        var payload = _subscriber.Events.Single(e => e.EventName == DiagnosticEventNames.CorrelationResolved).Payload;
        DiagnosticListenerSubscriber.GetPayloadValue<string>(payload, "CorrelationId").Should().Be(id);
        DiagnosticListenerSubscriber.GetPayloadValue<string>(payload, "Source").Should().Be("caller");
    }

    [Fact]
    public void BeginOperationScope_WithOptions_EmitsCorrelationResolvedEvent()
    {
        var opts = new OperationScopeOptions { CorrelationId = "opts-id-xyz" };

        using var scope = _factory.BeginOperationScope(opts);

        _subscriber.Events.Should().ContainSingle(e => e.EventName == DiagnosticEventNames.CorrelationResolved);
        var payload = _subscriber.Events.Single(e => e.EventName == DiagnosticEventNames.CorrelationResolved).Payload;
        DiagnosticListenerSubscriber.GetPayloadValue<string>(payload, "CorrelationId").Should().Be("opts-id-xyz");
    }

    public void Dispose() => _subscriber.Dispose();

    private static LogScopeFactory BuildFactory()
    {
        var pipeline = new EnrichmentPipeline([], [], NullLogger<EnrichmentPipeline>.Instance);
        return new LogScopeFactory(NullLogger<LogScopeFactory>.Instance, pipeline, new MicroKitLoggingOptions());
    }
}
