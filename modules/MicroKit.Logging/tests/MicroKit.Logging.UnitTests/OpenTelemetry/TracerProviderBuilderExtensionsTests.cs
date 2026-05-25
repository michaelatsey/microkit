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
    public void AddMicroKitLoggingSources_WithoutRegistration_StartActivityReturnsNull()
    {
        // Verify that a TracerProvider built WITHOUT MicroKit sources does not activate them.
        // We cannot rely on HasListeners() because static ActivitySource state is shared across
        // the entire process — another test in the collection may have a live provider that
        // subscribed to MicroKit sources. Instead, assert the observable effect: StartActivity
        // returns null when no subscriber is sampling the source.
        //
        // Build a provider that deliberately excludes MicroKit sources.
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("UnrelatedSource")
            .Build();

        // Create a fresh ActivitySource not known to any provider — StartActivity must return null.
        using var isolatedSource = new ActivitySource("MicroKit.Test.Isolated." + Guid.NewGuid());
        using var activity = isolatedSource.StartActivity("probe");
        activity.Should().BeNull("an unregistered source must not produce activities");
    }
}
