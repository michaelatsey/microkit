using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MicroKit.Persistence.Testing;

/// <summary>
/// Tells whether a Docker daemon answers on this machine, so a test that needs a database container can be
/// skipped, in the test framework's own way, rather than failed. Framework-neutral: ask
/// <see cref="IsAvailable"/>.
/// </summary>
/// <remarks>
/// <para>
/// A file at the socket path proves nothing: a socket left behind by a stopped daemon, or a regular file,
/// is still a file. The probe connects to the endpoint, sends the Engine API's <c>GET /_ping</c> and
/// reports a daemon only on an HTTP 200. It launches no process, the connect and the whole ping exchange each
/// run under a 2-second deadline, and any failure means "no daemon": it never throws.
/// </para>
/// <para>
/// Sources, in order: <c>DOCKER_HOST</c> (<c>unix://</c>, <c>tcp://</c>, <c>npipe://</c>) when it is set,
/// and only it; otherwise <c>/var/run/docker.sock</c>, then <c>$XDG_RUNTIME_DIR/docker.sock</c> (rootless),
/// then the Windows pipe <c>docker_engine</c>. With <c>DOCKER_HOST=tcp://…</c> and <c>DOCKER_TLS_VERIFY</c>
/// set, the daemon requires the client certificate, which the probe does not load: an accepted TCP
/// connection counts there, without a ping, so any listener on that port passes.
/// </para>
/// <para>
/// Testcontainers finds a daemon through more sources than these (<c>~/.testcontainers.properties</c>,
/// Docker contexts, Docker Desktop's socket). Where only those reach the daemon the probe answers
/// <see langword="false"/> and the test is skipped although it could have run; the fixtures never consult
/// the probe, so a disagreement can skip a test but never fail one.
/// </para>
/// </remarks>
public static class DockerProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly byte[] PingRequest =
        Encoding.ASCII.GetBytes("GET /_ping HTTP/1.1\r\nHost: docker\r\nConnection: close\r\n\r\n");

    private static readonly Lazy<bool> Detected = new(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets a value indicating whether a Docker daemon answers.</summary>
    /// <remarks>
    /// The first read probes the sources in order and blocks for up to about 4 seconds per source tried (a
    /// connect and a ping, 2 seconds each); the answer is then cached for the life of the process. Never
    /// throws.
    /// </remarks>
    public static bool IsAvailable => Detected.Value;

    /// <summary>Probes the Docker daemon listening on the Unix socket at <paramref name="path"/>.</summary>
    internal static bool ProbeUnixSocket(string path) =>
        Try(() => Probe(new UnixDomainSocketEndPoint(path)));

    /// <summary>
    /// Connects to <paramref name="endpoint"/>, sends <c>GET /_ping</c> and reports whether the answer is an
    /// HTTP 200. Never throws.
    /// </summary>
    internal static bool Probe(EndPoint endpoint) => Try(() => RunBlocking(() => ProbeAsync(endpoint)));

    /// <summary>
    /// Probes the endpoint a <c>DOCKER_HOST</c> value names. A TLS daemon (<c>DOCKER_TLS_VERIFY</c>) cannot
    /// be pinged in plain HTTP without the client certificate: an accepted TCP connection is what counts
    /// there. Never throws.
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

    // A TCP connection the endpoint accepts within the timeout.
    private static bool Connects(EndPoint endpoint) => Try(() => RunBlocking(async () =>
    {
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        using var connecting = new CancellationTokenSource(Timeout);
        await socket.ConnectAsync(endpoint, connecting.Token).ConfigureAwait(false);
        return socket.Connected;
    }));

    private static bool ProbeNamedPipe(string pipeName) => Try(() => RunBlocking(async () =>
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using (var connecting = new CancellationTokenSource(Timeout))
            await pipe.ConnectAsync(connecting.Token).ConfigureAwait(false);
        return await PingAsync(pipe).ConfigureAwait(false);
    }));

    /// <summary>
    /// Sends the ping and reads the status line ("HTTP/1.x 200 …"), the whole exchange under one 2-second
    /// deadline. Each operation is abandoned at the deadline even when the stream ignores cancellation — a
    /// named pipe has no read timeout. Never throws.
    /// </summary>
    internal static bool Ping(Stream stream) => Try(() => RunBlocking(() => PingAsync(stream)));

    // The documented exception to "no sync-over-async" (rules/performance.md): IsAvailable is a cached,
    // once-per-process check whose public API must be synchronous — a test framework's skip condition reads it
    // in an attribute constructor — and only the asynchronous socket and pipe calls bound every wait. Task.Run
    // runs the probe on the thread pool, so no SynchronizationContext the caller holds can deadlock it.
    private static bool RunBlocking(Func<Task<bool>> probe) => Task.Run(probe).GetAwaiter().GetResult();

    private static async Task<bool> ProbeAsync(EndPoint endpoint)
    {
        using var socket = endpoint is UnixDomainSocketEndPoint
            ? new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            : new Socket(SocketType.Stream, ProtocolType.Tcp);
        using (var connecting = new CancellationTokenSource(Timeout))
            await socket.ConnectAsync(endpoint, connecting.Token).ConfigureAwait(false);

        using var stream = new NetworkStream(socket, ownsSocket: false);
        return await PingAsync(stream).ConfigureAwait(false);
    }

    private static async Task<bool> PingAsync(Stream stream)
    {
        using var deadline = new CancellationTokenSource(Timeout);
        await stream.WriteAsync(PingRequest, deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
        await stream.FlushAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);

        var buffer = new byte[256];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), deadline.Token).AsTask()
                .WaitAsync(deadline.Token).ConfigureAwait(false);
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
