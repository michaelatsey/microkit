using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.Docker;

/// <summary>
/// A <see cref="FactAttribute"/> that skips — rather than fails — when no Docker daemon answers.
/// </summary>
/// <remarks>
/// <para>
/// The database suites need a real server, and a developer without Docker must still be able to
/// run <c>dotnet test</c> and get green. Setting <see cref="FactAttribute.Skip"/> from a derived
/// attribute is the xunit v2 idiom for a conditional test and needs no extra package.
/// </para>
/// <para>
/// The probe runs at discovery time, once per test run (<see cref="DockerEndpoint"/>), so it must
/// stay cheap and total: it launches no process and throws nothing.
/// </para>
/// </remarks>
public sealed class DockerRequiredFactAttribute : FactAttribute
{
    public DockerRequiredFactAttribute()
    {
        if (!DockerEndpoint.IsAvailable)
            Skip = DockerEndpoint.SkipReason;
    }
}

/// <summary>
/// The <see cref="TheoryAttribute"/> counterpart of <see cref="DockerRequiredFactAttribute"/>:
/// skips every data row when no Docker daemon answers.
/// </summary>
public sealed class DockerRequiredTheoryAttribute : TheoryAttribute
{
    public DockerRequiredTheoryAttribute()
    {
        if (!DockerEndpoint.IsAvailable)
            Skip = DockerEndpoint.SkipReason;
    }
}

/// <summary>Cheap, total, side-effect-free detection of a Docker daemon.</summary>
/// <remarks>
/// <para>
/// A file at the socket path proves nothing: a socket left behind by a stopped daemon, or a
/// regular file, is still a file. The probe therefore connects to the endpoint and sends the
/// Engine API's <c>GET /_ping</c>, and reports a daemon only on an HTTP 200. Every step runs under
/// a short timeout and any failure means "no daemon".
/// </para>
/// <para>
/// One fallback: with <c>DOCKER_HOST=tcp://…</c> and <c>DOCKER_TLS_VERIFY</c> set, the daemon
/// speaks TLS and requires the client certificate, which the probe does not load. There an
/// accepted TCP connection counts as available, without a ping: a stopped daemon refuses the
/// connection, but a listener that is not a Docker daemon would be taken for one.
/// </para>
/// </remarks>
internal static class DockerEndpoint
{
    internal const string SkipReason =
        "No Docker daemon answers — skipping the database suites (PostgreSQL, SQL Server). " +
        "Start Docker to run the tests against real databases.";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly byte[] PingRequest =
        Encoding.ASCII.GetBytes("GET /_ping HTTP/1.1\r\nHost: docker\r\nConnection: close\r\n\r\n");

    private static readonly Lazy<bool> Detected = new(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets a value indicating whether a Docker daemon answers.</summary>
    internal static bool IsAvailable => Detected.Value;

    /// <summary>Probes the Docker daemon listening on the Unix socket at <paramref name="path"/>.</summary>
    internal static bool ProbeUnixSocket(string path) =>
        Try(() => Probe(new UnixDomainSocketEndPoint(path)));

    /// <summary>
    /// Connects to <paramref name="endpoint"/>, sends <c>GET /_ping</c> and reports whether the
    /// answer is an HTTP 200. Never throws.
    /// </summary>
    internal static bool Probe(EndPoint endpoint) => Try(() =>
    {
        using var socket = endpoint is UnixDomainSocketEndPoint
            ? new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            : new Socket(SocketType.Stream, ProtocolType.Tcp);
        socket.SendTimeout = (int)Timeout.TotalMilliseconds;
        socket.ReceiveTimeout = (int)Timeout.TotalMilliseconds;

        using (var connecting = new CancellationTokenSource(Timeout))
            socket.ConnectAsync(endpoint, connecting.Token).AsTask().GetAwaiter().GetResult();

        using var stream = new NetworkStream(socket, ownsSocket: false);
        return Ping(stream);
    });

    // A TCP connection the endpoint accepts within the timeout.
    private static bool Connects(EndPoint endpoint) => Try(() =>
    {
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        using var connecting = new CancellationTokenSource(Timeout);
        socket.ConnectAsync(endpoint, connecting.Token).AsTask().GetAwaiter().GetResult();
        return socket.Connected;
    });

    private static bool Detect()
    {
        // An explicit endpoint always wins — including remote and rootless setups.
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost))
            return ProbeDockerHost(dockerHost, tlsVerify: !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_TLS_VERIFY")));

        // Linux, WSL2 and macOS.
        if (ProbeUnixSocket("/var/run/docker.sock"))
            return true;

        // Rootless Linux: $XDG_RUNTIME_DIR/docker.sock.
        var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdg) && ProbeUnixSocket(Path.Combine(xdg, "docker.sock")))
            return true;

        // Windows named pipe.
        return OperatingSystem.IsWindows() && ProbeNamedPipe("docker_engine");
    }

    /// <summary>
    /// Probes the endpoint a <c>DOCKER_HOST</c> value names. A TLS daemon (<c>DOCKER_TLS_VERIFY</c>)
    /// cannot be pinged in plain HTTP without the client certificate: an accepted TCP connection is
    /// what counts there.
    /// </summary>
    internal static bool ProbeDockerHost(string dockerHost, bool tlsVerify) => Try(() =>
    {
        var uri = new Uri(dockerHost);
        return uri.Scheme switch
        {
            "unix" => ProbeUnixSocket(uri.AbsolutePath),
            "tcp" when tlsVerify => Connects(new DnsEndPoint(uri.Host, uri.Port)),
            "tcp" => Probe(new DnsEndPoint(uri.Host, uri.Port)),
            "npipe" => OperatingSystem.IsWindows() && ProbeNamedPipe(uri.AbsolutePath.Split('/')[^1]),
            _ => false,
        };
    });

    private static bool ProbeNamedPipe(string pipeName) => Try(() =>
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        pipe.Connect(Timeout);
        return Ping(pipe);
    });

    // Sends the ping and reads the status line: "HTTP/1.x 200 …".
    private static bool Ping(Stream stream)
    {
        stream.Write(PingRequest);
        stream.Flush();

        var buffer = new byte[256];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = stream.Read(buffer, length, buffer.Length - length);
            if (read == 0)
                break;
            length += read;
            if (Array.IndexOf(buffer, (byte)'\n', 0, length) >= 0)
                break;
        }

        var statusLine = Encoding.ASCII.GetString(buffer, 0, length).Split('\n')[0];
        var parts = statusLine.Split(' ', 3);
        return parts.Length >= 2 && parts[0].StartsWith("HTTP/1.", StringComparison.Ordinal) && parts[1] == "200";
    }

    private static bool Try(Func<bool> probe)
    {
#pragma warning disable CA1031 // the probe is total: any failure means "no daemon"
        try
        {
            return probe();
        }
        catch (Exception)
        {
            return false;
        }
#pragma warning restore CA1031
    }
}
