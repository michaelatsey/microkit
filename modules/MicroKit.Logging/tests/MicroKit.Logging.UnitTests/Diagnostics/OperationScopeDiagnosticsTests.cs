using Microsoft.Extensions.Logging.Abstractions;
using MicroKit.Logging.Internal;

namespace MicroKit.Logging.UnitTests.Diagnostics;

[Collection("DiagnosticListener")]
public sealed class OperationScopeDiagnosticsTests : IDisposable
{
    private readonly DiagnosticListenerSubscriber _subscriber;
    private readonly LogScopeFactory _factory;

    public OperationScopeDiagnosticsTests()
    {
        _subscriber = new DiagnosticListenerSubscriber("MicroKit.Logging");
        _factory = BuildFactory();
    }

    [Fact]
    public void BeginOperationScope_EmitsScopeCreatedEvent()
    {
        using var scope = _factory.BeginOperationScope();

        _subscriber.Events.ShouldContain(e => e.EventName == DiagnosticEventNames.ScopeCreated);
        var payload = _subscriber.Events.Single(e => e.EventName == DiagnosticEventNames.ScopeCreated).Payload;
        DiagnosticListenerSubscriber.GetPayloadValue<string>(payload, "CorrelationId").ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void DisposeScope_EmitsScopeDisposedEvent()
    {
        var scope = _factory.BeginOperationScope();

        scope.Dispose();

        _subscriber.Events.ShouldContain(e => e.EventName == DiagnosticEventNames.ScopeDisposed);
        var payload = _subscriber.Events.Single(e => e.EventName == DiagnosticEventNames.ScopeDisposed).Payload;
        DiagnosticListenerSubscriber.GetPayloadValue<double>(payload, "DurationMs").ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void BeginOperationScope_WithProvidedId_ScopeCreatedPayloadHasCorrectCorrelationId()
    {
        const string expectedId = "test-correlation-123";

        using var scope = _factory.BeginOperationScope(expectedId);

        var payload = _subscriber.Events.Single(e => e.EventName == DiagnosticEventNames.ScopeCreated).Payload;
        DiagnosticListenerSubscriber.GetPayloadValue<string>(payload, "CorrelationId").ShouldBe(expectedId);
    }

    public void Dispose() => _subscriber.Dispose();

    private static LogScopeFactory BuildFactory()
    {
        var pipeline = new EnrichmentPipeline([], [], NullLogger<EnrichmentPipeline>.Instance);
        return new LogScopeFactory(NullLogger<LogScopeFactory>.Instance, pipeline, new MicroKitLoggingOptions());
    }
}
