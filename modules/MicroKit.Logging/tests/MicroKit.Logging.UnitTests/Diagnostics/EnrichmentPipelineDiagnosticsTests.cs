using Microsoft.Extensions.Logging.Abstractions;
using MicroKit.Logging.Internal;

namespace MicroKit.Logging.UnitTests.Diagnostics;

[Collection("DiagnosticListener")]
public sealed class EnrichmentPipelineDiagnosticsTests : IDisposable
{
    private readonly DiagnosticListenerSubscriber _subscriber;

    public EnrichmentPipelineDiagnosticsTests()
    {
        _subscriber = new DiagnosticListenerSubscriber("MicroKit.Logging");
    }

    [Fact]
    public void Execute_WhenPipelineSucceeds_EmitsEnrichmentExecutedEvent()
    {
        var pipeline = BuildPipeline(new SuccessEnricher());
        var context = new LogEnrichmentContext(8);

        pipeline.Execute(context);

        _subscriber.Events.Should().ContainSingle(e => e.EventName == DiagnosticEventNames.EnrichmentExecuted);
        var payload = _subscriber.Events.Single(e => e.EventName == DiagnosticEventNames.EnrichmentExecuted).Payload;
        DiagnosticListenerSubscriber.GetPayloadValue<int>(payload, "EnricherCount").Should().Be(1);
        DiagnosticListenerSubscriber.GetPayloadValue<double>(payload, "ElapsedMs").Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Execute_WhenEnricherThrows_EmitsEnrichmentFaultedEvent()
    {
        var pipeline = BuildPipeline(new ThrowingEnricher());
        var context = new LogEnrichmentContext(8);

        pipeline.Execute(context);

        _subscriber.Events.Should().ContainSingle(e => e.EventName == DiagnosticEventNames.EnrichmentFaulted);
        var payload = _subscriber.Events.Single(e => e.EventName == DiagnosticEventNames.EnrichmentFaulted).Payload;
        DiagnosticListenerSubscriber.GetPayloadValue<string>(payload, "EnricherType").Should().Be(nameof(ThrowingEnricher));
        DiagnosticListenerSubscriber.GetPayloadValue<Exception>(payload, "Exception").Should().NotBeNull();
    }

    [Fact]
    public void Execute_WithMixedEnrichers_EmitsBothFaultedAndExecuted()
    {
        var pipeline = BuildPipeline(new ThrowingEnricher(), new SuccessEnricher());
        var context = new LogEnrichmentContext(8);

        pipeline.Execute(context);

        _subscriber.Events.Should().Contain(e => e.EventName == DiagnosticEventNames.EnrichmentFaulted);
        _subscriber.Events.Should().Contain(e => e.EventName == DiagnosticEventNames.EnrichmentExecuted);
        var executedPayload = _subscriber.Events.Single(e => e.EventName == DiagnosticEventNames.EnrichmentExecuted).Payload;
        DiagnosticListenerSubscriber.GetPayloadValue<int>(executedPayload, "EnricherCount").Should().Be(2);
    }

    public void Dispose() => _subscriber.Dispose();

    private static EnrichmentPipeline BuildPipeline(params ILogEnricher[] enrichers)
        => new(enrichers, [], NullLogger<EnrichmentPipeline>.Instance);

    private sealed class SuccessEnricher : ILogEnricher
    {
        public int Order => 0;
        public void Enrich(ILogEnrichmentContext context) { }
    }

    private sealed class ThrowingEnricher : ILogEnricher
    {
        public int Order => 0;
        public void Enrich(ILogEnrichmentContext context) => throw new InvalidOperationException("test fault");
    }
}
