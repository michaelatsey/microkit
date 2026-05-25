using MicroKit.Logging.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace MicroKit.Logging.UnitTests.OpenTelemetry;

[Collection("DiagnosticListener")]
public sealed class TracerProviderBuilderExtensionsTests
{
    [Fact]
    public void AddMicroKitLoggingSources_ReturnsSameBuilderInstance()
    {
        var builder = Sdk.CreateTracerProviderBuilder();

        var result = builder.AddMicroKitLoggingSources();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddMicroKitLoggingSources_EnablesLoggingActivitySource()
    {
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddMicroKitLoggingSources()
            .Build();

        // After registration, the source should have listeners
        MicroKitActivitySources.Logging.HasListeners().Should().BeTrue();
    }

    [Fact]
    public void AddMicroKitLoggingSources_EnablesEnrichmentActivitySource()
    {
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddMicroKitLoggingSources()
            .Build();

        MicroKitActivitySources.Enrichment.HasListeners().Should().BeTrue();
    }

    [Fact]
    public void AddMicroKitLoggingSources_WithoutRegistration_SourceHasNoListeners()
    {
        // Baseline: without calling AddMicroKitLoggingSources, sources have no OTEL listeners
        // (other tests in the same run may affect this, so we use a separate provider scope)
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("UnrelatedSource")
            .Build();

        // After building a provider that did NOT include MicroKit sources,
        // MicroKit sources should still not have listeners from this provider.
        // Note: this test is meaningful only when run in isolation; the collection
        // serialization ensures no cross-test interference.
        MicroKitActivitySources.Logging.HasListeners().Should().BeFalse();
    }
}
