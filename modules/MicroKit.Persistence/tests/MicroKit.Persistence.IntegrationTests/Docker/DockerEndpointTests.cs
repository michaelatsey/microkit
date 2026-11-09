using System.Net;
using System.Net.Sockets;
using System.Text;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.Docker;

/// <summary>
/// #197 phase 1: the Docker probe reports a daemon only when one answers, so a leftover socket
/// file makes the database suites skip instead of fail. These tests need no Docker.
/// </summary>
public sealed class DockerEndpointTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("mk-docker-probe-").FullName;

    [Fact]
    public void ProbeUnixSocket_WhenTheSocketFileIsStale_ReportsNoDaemon()
    {
        var path = Path.Combine(_directory, "docker.sock");
        var bound = Path.Combine(_directory, "bound.sock");
        using (var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            listener.Bind(new UnixDomainSocketEndPoint(bound));
            listener.Listen();

            // .NET unlinks the path it bound when the socket closes; a daemon that dies does not.
            // Renaming the socket file keeps it past the close: the stale socket a crash leaves.
            File.Move(bound, path);
        }

        File.Exists(path).ShouldBeTrue("closing a bound Unix socket leaves its file behind: the stale case");
        DockerEndpoint.ProbeUnixSocket(path).ShouldBeFalse();
    }

    [Fact]
    public void ProbeUnixSocket_WhenThePathIsARegularFile_ReportsNoDaemon()
    {
        var path = Path.Combine(_directory, "docker.sock");
        File.WriteAllText(path, "not a socket");

        DockerEndpoint.ProbeUnixSocket(path).ShouldBeFalse();
    }

    [Fact]
    public void ProbeUnixSocket_WhenNothingExistsAtThePath_ReportsNoDaemon_WithoutThrowing()
    {
        Should.NotThrow(() => DockerEndpoint.ProbeUnixSocket(Path.Combine(_directory, "missing", "docker.sock")))
            .ShouldBeFalse();
        Should.NotThrow(() => DockerEndpoint.ProbeUnixSocket("\0invalid")).ShouldBeFalse();
    }

    [Fact]
    public void Probe_WhenNothingListensOnTheTcpEndpoint_ReportsNoDaemon_WithoutThrowing()
    {
        // Bind then release an ephemeral port: nothing listens there afterwards.
        int port;
        using (var reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            port = ((IPEndPoint)reserved.LocalEndPoint!).Port;
        }

        Should.NotThrow(() => DockerEndpoint.Probe(new IPEndPoint(IPAddress.Loopback, port))).ShouldBeFalse();
    }

    [Fact]
    public async Task ProbeDockerHost_OnATlsTcpEndpoint_ReportsADaemonThatAcceptsTheConnection()
    {
        // A TLS daemon (DOCKER_TLS_VERIFY) cannot be pinged in plain HTTP: an accepted TCP connection
        // is what the probe can verify without the client certificate.
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen();
        var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
        var accepted = listener.AcceptAsync();

        DockerEndpoint.ProbeDockerHost($"tcp://127.0.0.1:{port}", tlsVerify: true).ShouldBeTrue();
        using var connection = await accepted;
    }

    [Fact]
    public void ProbeDockerHost_OnATlsTcpEndpointWithNothingListening_ReportsNoDaemon()
    {
        int port;
        using (var reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            port = ((IPEndPoint)reserved.LocalEndPoint!).Port;
        }

        DockerEndpoint.ProbeDockerHost($"tcp://127.0.0.1:{port}", tlsVerify: true).ShouldBeFalse();
    }

    [Fact]
    public async Task ProbeUnixSocket_WhenADaemonAnswersThePing_ReportsIt()
    {
        var path = Path.Combine(_directory, "docker.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen();
        var daemon = AnswerOnePingAsync(listener, "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK");

        DockerEndpoint.ProbeUnixSocket(path).ShouldBeTrue();
        (await daemon).ShouldStartWith("GET /_ping ");
    }

    [Fact]
    public async Task ProbeUnixSocket_WhenTheListenerDoesNotAnswer200_ReportsNoDaemon()
    {
        var path = Path.Combine(_directory, "docker.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen();
        var daemon = AnswerOnePingAsync(listener, "HTTP/1.1 500 Internal Server Error\r\nContent-Length: 0\r\n\r\n");

        DockerEndpoint.ProbeUnixSocket(path).ShouldBeFalse();
        await daemon;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // Accepts one connection, reads the request line, writes the canned response and closes.
    private static async Task<string> AnswerOnePingAsync(Socket listener, string response)
    {
        using var connection = await listener.AcceptAsync();
        var buffer = new byte[1024];
        var read = await connection.ReceiveAsync(buffer, SocketFlags.None);
        await connection.SendAsync(Encoding.ASCII.GetBytes(response), SocketFlags.None);
        connection.Shutdown(SocketShutdown.Both);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }
}
