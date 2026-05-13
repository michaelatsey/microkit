using MicroKit.Resilience.Abstractions;
using MicroKit.Resilience;
using MicroKit.Resilience.Data.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MicroKit.Resilience.Tests;

/// <summary>
/// Unit tests for SQL Server resilience detection.
/// </summary>
public class SqlResilienceDetectorTests
{
    private readonly SqlResilienceDetector _detector = new();

    [Fact]
    public void CanHandle_WithoutSqlException_ReturnsFalse()
    {
        // Arrange
        var ex = new InvalidOperationException("No SQL exception");

        // Act
        var result = _detector.CanHandle(ex);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldRetry_WithNonSqlException_ReturnsFalse()
    {
        // Arrange
        var ex = new InvalidOperationException("Not a SQL exception");

        // Act
        var result = _detector.ShouldRetry(ex);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Detector_Implements_IResilienceStrategyDetector()
    {
        // Act & Assert
        Assert.IsAssignableFrom<IResilienceStrategyDetector>(_detector);
    }

    [Fact]
    public void Detector_Implements_IResilienceErrorDetector()
    {
        // Act & Assert
        Assert.IsAssignableFrom<IResilienceErrorDetector>(_detector);
    }
}
