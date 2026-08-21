namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// A <see cref="FactAttribute"/> that skips — rather than fails — when no Docker endpoint is
/// reachable.
/// </summary>
/// <remarks>
/// <para>
/// The PostgreSQL suite needs a real database, and a developer without Docker must still be able
/// to run <c>dotnet test</c> and get green. Setting <see cref="FactAttribute.Skip"/> from a derived
/// attribute is the xunit v2 idiom for a conditional test and needs no extra package.
/// </para>
/// <para>
/// The probe deliberately launches no process and throws nothing: it runs at discovery time, for
/// every test, so it must be cheap and total. It checks the same places a Docker client would.
/// </para>
/// </remarks>
public sealed class DockerRequiredFactAttribute : FactAttribute
{
    public DockerRequiredFactAttribute()
    {
        if (!DockerEndpoint.IsAvailable)
        {
            Skip = "Docker is not available — skipping the PostgreSQL suite. " +
                   "Start Docker to run the outbox concurrency tests against a real database.";
        }
    }
}

/// <summary>Cheap, total, side-effect-free detection of a usable Docker endpoint.</summary>
internal static class DockerEndpoint
{
    private static readonly Lazy<bool> Probe = new(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets a value indicating whether a Docker endpoint looks reachable.</summary>
    internal static bool IsAvailable => Probe.Value;

    private static bool Detect()
    {
        try
        {
            // An explicit endpoint always wins — including remote and rootless setups.
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
            {
                return true;
            }

            // Linux, WSL2 and macOS.
            if (File.Exists("/var/run/docker.sock"))
            {
                return true;
            }

            // Rootless Linux: $XDG_RUNTIME_DIR/docker.sock.
            var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrWhiteSpace(xdg) && File.Exists(Path.Combine(xdg, "docker.sock")))
            {
                return true;
            }

            // Windows named pipe.
            return OperatingSystem.IsWindows() && File.Exists(@"\\.\pipe\docker_engine");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A probe that cannot answer must report "no Docker", never take the suite down.
            return false;
        }
    }
}
