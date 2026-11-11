namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The skip decision of <see cref="DockerRequiredFactAttribute"/>, without Docker: the Docker
/// half of the probe is Testcontainers' own discovery and is proven against a real, stopped and
/// running daemon (#173), not mocked here.
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
}
