using MicroKit.Resilience.Abstractions;
using MicroKit.Resilience;
using MicroKit.Resilience.Data.PostgreSQL;
using Npgsql;
using Xunit;

namespace MicroKit.Resilience.Tests;

/// <summary>
/// Unit tests for PostgreSQL resilience detection.
/// </summary>
public class PostgresResilienceDetectorTests
{
    private readonly PostgresResilienceDetector _detector = new();

    [Fact]
    public void CanHandle_WithNpgsqlException_ReturnsTrue()
    {
        // Arrange
        var ex = new NpgsqlException("Connection failed");

        // Act
        var result = _detector.CanHandle(ex);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanHandle_WithNpgsqlExceptionAsInner_ReturnsTrue()
    {
        // Arrange
        var pgEx = new NpgsqlException("Connection failed");
        var ex = new InvalidOperationException("Outer", pgEx);

        // Act
        var result = _detector.CanHandle(ex);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanHandle_WithoutNpgsqlException_ReturnsFalse()
    {
        // Arrange
        var ex = new InvalidOperationException("No Postgres exception");

        // Act
        var result = _detector.CanHandle(ex);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldRetry_WithNpgsqlException_WithoutTransientSqlState_ReturnsFalse()
    {
        // Arrange — NpgsqlException with no SqlState (null) does not match any transient code
        var pgEx = new NpgsqlException("Generic error");

        // Act
        var result = _detector.ShouldRetry(pgEx);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldRetry_WithNonTransientException_ReturnsFalse()
    {
        // Arrange
        var ex = new InvalidOperationException("Not a Postgres exception");

        // Act
        var result = _detector.ShouldRetry(ex);

        // Assert
        Assert.False(result);
    }
}
