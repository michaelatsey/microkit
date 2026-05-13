using MicroKit.Resilience.Abstractions;
using MicroKit.Resilience.Http;
using Xunit;

namespace MicroKit.Resilience.Tests;

/// <summary>
/// Unit tests for HTTP resilience detection.
/// </summary>
public class HttpResilienceDetectorTests
{
    private readonly HttpResilienceDetector _detector = new();

    [Fact]
    public void CanHandle_WithHttpRequestException_ReturnsTrue()
    {
        // Arrange
        var ex = new HttpRequestException("Connection failed");

        // Act
        var result = _detector.CanHandle(ex);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanHandle_WithTaskCanceledException_ReturnsTrue()
    {
        // Arrange
        var ex = new TaskCanceledException("Timeout");

        // Act
        var result = _detector.CanHandle(ex);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanHandle_WithSocketException_ReturnsTrue()
    {
        // Arrange
        var ex = new System.Net.Sockets.SocketException(10054); // Connection reset

        // Act
        var result = _detector.CanHandle(ex);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanHandle_WithOtherException_ReturnsFalse()
    {
        // Arrange
        var ex = new InvalidOperationException("Some error");

        // Act
        var result = _detector.CanHandle(ex);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldRetry_WithServerErrorStatusCode_ReturnsTrue()
    {
        // Arrange
        var ex = new HttpRequestException("Server error", null, System.Net.HttpStatusCode.InternalServerError);

        // Act
        var result = _detector.ShouldRetry(ex);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldRetry_WithNoStatusCode_ReturnsTrue()
    {
        // Arrange
        var ex = new HttpRequestException("Connection failed");

        // Act
        var result = _detector.ShouldRetry(ex);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldRetry_WithClientErrorStatusCode_ReturnsFalse()
    {
        // Arrange
        var ex = new HttpRequestException("Not found", null, System.Net.HttpStatusCode.NotFound);

        // Act
        var result = _detector.ShouldRetry(ex);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldRetry_WithTaskCanceledDueToTimeout_ReturnsTrue()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var ex = new TaskCanceledException("Timeout", new OperationCanceledException(cts.Token));

        // Act
        var result = _detector.ShouldRetry(ex);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldRetry_WithSocketException_ReturnsTrue()
    {
        // Arrange
        var ex = new System.Net.Sockets.SocketException(10054);

        // Act
        var result = _detector.ShouldRetry(ex);

        // Assert
        Assert.True(result);
    }
}
