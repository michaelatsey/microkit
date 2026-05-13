using MicroKit.Resilience.Builder;
using Xunit;

namespace MicroKit.Resilience.Tests;

/// <summary>
/// Unit tests for ResilienceRetryOptions.
/// </summary>
public class ResilienceRetryOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new ResilienceRetryOptions();

        // Assert
        Assert.Equal(3, options.RetryCount);
        Assert.Equal("DefaultRetry", options.PipelineName);
        Assert.Equal(1.0, options.BaseDelaySeconds);
        Assert.True(options.EnableFallback);
        Assert.True(options.EnableCircuitBreaker);
        Assert.Equal(0.5, options.FailureRatio);
        Assert.Equal(10, options.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(30), options.BreakDuration);
    }

    [Fact]
    public void FailureRatio_CanBeSet()
    {
        // Arrange
        var options = new ResilienceRetryOptions { FailureRatio = 0.7 };

        // Act & Assert
        Assert.Equal(0.7, options.FailureRatio);
    }

    [Fact]
    public void RetryCount_CanBeSet()
    {
        // Arrange
        var options = new ResilienceRetryOptions { RetryCount = 5 };

        // Act & Assert
        Assert.Equal(5, options.RetryCount);
    }

    [Fact]
    public void PipelineName_CanBeSet()
    {
        // Arrange
        var options = new ResilienceRetryOptions { PipelineName = "CustomPipeline" };

        // Act & Assert
        Assert.Equal("CustomPipeline", options.PipelineName);
    }

    [Fact]
    public void BreakDuration_CanBeSet()
    {
        // Arrange
        var duration = TimeSpan.FromSeconds(60);
        var options = new ResilienceRetryOptions { BreakDuration = duration };

        // Act & Assert
        Assert.Equal(duration, options.BreakDuration);
    }

}
