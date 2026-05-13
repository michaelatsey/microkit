using MicroKit.Resilience.Abstractions;
using MicroKit.Resilience.Builder;
using MicroKit.Resilience.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MicroKit.Resilience.Tests;

/// <summary>
/// Unit tests for resilience service collection extensions.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMicroKitResilience_RegistersBuilder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = services.AddMicroKitResilience();

        // Assert
        Assert.NotNull(builder);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddMicroKitResilience_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        ServiceCollection? services = null;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            services!.AddMicroKitResilience());
        Assert.Equal("services", ex.ParamName);
    }

    [Fact]
    public void AddSqlServer_RegistersSqlDetector()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddMicroKitResilience();

        // Act
        builder.AddSqlServer();

        // Assert
        var provider = services.BuildServiceProvider();
        var strategies = provider.GetRequiredService<IEnumerable<IResilienceStrategyDetector>>();
        Assert.NotEmpty(strategies);
    }

    [Fact]
    public void AddHttp_RegistersHttpDetector()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddMicroKitResilience();

        // Act
        builder.AddHttp();

        // Assert
        var provider = services.BuildServiceProvider();
        var strategies = provider.GetRequiredService<IEnumerable<IResilienceStrategyDetector>>();
        Assert.NotEmpty(strategies);
    }

    [Fact]
    public void AddPostgres_RegistersPostgresDetector()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddMicroKitResilience();

        // Act
        builder.AddPostgres();

        // Assert
        var provider = services.BuildServiceProvider();
        var strategies = provider.GetRequiredService<IEnumerable<IResilienceStrategyDetector>>();
        Assert.NotEmpty(strategies);
    }
}
