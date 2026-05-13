using MicroKit.Resilience.Abstractions;
using MicroKit.Resilience;
using MicroKit.Resilience.Data.SqlServer;
using MicroKit.Resilience.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MicroKit.Resilience.Tests;

/// <summary>
/// Unit tests for CompositeResilienceDetector.
/// </summary>
public class CompositeResilienceDetectorTests
{
    [Fact]
    public void ShouldRetry_WithMultipleDetectors_ChecksAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IResilienceStrategyDetector, HttpResilienceDetector>();
        services.AddSingleton<IResilienceStrategyDetector, SqlResilienceDetector>();

        var detectors = services.BuildServiceProvider().GetRequiredService<IEnumerable<IResilienceStrategyDetector>>();
        var composite = new CompositeResilienceDetector(detectors);

        var httpEx = new HttpRequestException("Server error", null, System.Net.HttpStatusCode.InternalServerError);

        // Act
        var result = composite.ShouldRetry(httpEx);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldRetry_WithNoMatchingDetector_ReturnsFalse()
    {
        // Arrange
        var detectors = new List<IResilienceStrategyDetector>();
        var composite = new CompositeResilienceDetector(detectors);

        var ex = new InvalidOperationException("Unhandled exception");

        // Act
        var result = composite.ShouldRetry(ex);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Constructor_WithNullDetectors_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new CompositeResilienceDetector(null!));
        Assert.Equal("detectors", ex.ParamName);
    }
}
