using MicroKit.Resilience.Abstractions;
using MicroKit.Resilience.Builder;
using MicroKit.Resilience.Extensions;
using MicroKit.Resilience.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MicroKit.Resilience.Tests;

/// <summary>
/// Unit tests for MicroKitResilienceBuilder.
/// </summary>
public class MicroKitResilienceBuilderTests
{
    [Fact]
    public void Constructor_RegistersCompositeDetector()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = new MicroKitResilienceBuilder(services);

        // Assert
        var provider = services.BuildServiceProvider();
        var detector = provider.GetRequiredService<IResilienceErrorDetector>();
        Assert.NotNull(detector);
        Assert.IsType<CompositeResilienceDetector>(detector);
    }

    [Fact]
    public void AddDetector_RegistersStrategy()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MicroKitResilienceBuilder(services);

        // Act
        builder.AddDetector<HttpResilienceDetector>();

        // Assert
        var provider = services.BuildServiceProvider();
        var strategies = provider.GetRequiredService<IEnumerable<IResilienceStrategyDetector>>();
        Assert.Single(strategies);
        Assert.IsType<HttpResilienceDetector>(strategies.First());
    }

    [Fact]
    public void Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new MicroKitResilienceBuilder(null!));
        Assert.Equal("services", ex.ParamName);
    }

    [Fact]
    public void AddDetector_WithNullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        MicroKitResilienceBuilder? builder = null;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            MicroKit.Resilience.Extensions.HttpResilienceExtensions.AddHttp(builder!));
        Assert.Equal("builder", ex.ParamName);
    }
}
