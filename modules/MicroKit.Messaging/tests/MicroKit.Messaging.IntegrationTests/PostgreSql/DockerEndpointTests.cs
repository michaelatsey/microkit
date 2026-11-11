using System.Diagnostics;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The skip decision of <see cref="DockerRequiredFactAttribute"/> and the deadline on discovery,
/// without Docker: the Docker half of the probe is Testcontainers' own discovery and is proven
/// against a real, stopped and running daemon (#173), not mocked here.
/// </summary>
public sealed class DockerEndpointTests
{
    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, "", true)]
    [InlineData(true, "false", true)]
    [InlineData(true, "true", true)]
    [InlineData(true, "TRUE", true)]
    [InlineData(true, "1", true)]
    [InlineData(false, null, false)]
    [InlineData(false, "", false)]
    [InlineData(false, "false", false)]
    [InlineData(false, "true", true)]
    [InlineData(false, "TRUE", true)]
    [InlineData(false, "1", true)]
    public void ShouldRunFor_RunsWhenDockerIsAvailableOrCiForbidsSkipping(
        bool available, string? ci, bool expected)
    {
        DockerEndpoint.ShouldRunFor(available, ci).ShouldBe(expected);
    }

    [Fact]
    public void DiscoverWithin_WhenDiscoveryAnswersTrue_ReturnsTrue()
    {
        DockerEndpoint.DiscoverWithin(() => true, TimeSpan.FromSeconds(5)).ShouldBeTrue();
    }

    [Fact]
    public void DiscoverWithin_WhenDiscoveryThrows_ReturnsFalse()
    {
        DockerEndpoint.DiscoverWithin(
                () => throw new InvalidOperationException("discovery failed"),
                TimeSpan.FromSeconds(5))
            .ShouldBeFalse();
    }

    [Fact]
    public void DiscoverWithin_WhenDiscoveryOutlivesTheDeadline_ReturnsFalseWithoutWaitingForIt()
    {
        using var gate = new ManualResetEventSlim(initialState: false);
        using var exited = new ManualResetEventSlim(initialState: false);
        var clock = Stopwatch.StartNew();
        try
        {
            var available = DockerEndpoint.DiscoverWithin(
                () =>
                {
                    try
                    {
                        gate.Wait();
                        return true;
                    }
                    finally
                    {
                        exited.Set();
                    }
                },
                TimeSpan.FromMilliseconds(100));
            var elapsed = clock.Elapsed;

            available.ShouldBeFalse();
            elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
            // A lower bound too, or returning false without waiting at all would pass.
            elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(50));
        }
        finally
        {
            // Release the abandoned discovery and let it leave the gate before both are disposed.
            gate.Set();
            exited.Wait(TimeSpan.FromSeconds(5));
        }
    }
}
