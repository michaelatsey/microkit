using DotNet.Testcontainers.Configurations;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// A <see cref="FactAttribute"/> that skips — rather than fails — when no Docker daemon answers,
/// except under CI, where it never skips.
/// </summary>
/// <remarks>
/// <para>
/// The PostgreSQL suite needs a real database, and a developer without Docker must still be able
/// to run <c>dotnet test</c> and get green. Setting <see cref="FactAttribute.Skip"/> from a derived
/// attribute is the xunit v2 idiom for a conditional test and needs no extra package.
/// </para>
/// <para>
/// A CI run is the opposite case: a suite skipped there is a suite that silently stopped running,
/// with a green build to show for it. With the <c>CI</c> environment variable set to <c>true</c>
/// (any case) or <c>1</c> the attribute never skips, so a CI job without Docker fails loudly. See
/// <see cref="DockerEndpoint"/> for what "available" means.
/// </para>
/// </remarks>
public sealed class DockerRequiredFactAttribute : FactAttribute
{
    public DockerRequiredFactAttribute()
    {
        if (!DockerEndpoint.ShouldRun)
        {
            Skip = "Docker is not available — skipping the PostgreSQL suite. " +
                   "Start Docker to run the outbox and inbox concurrency tests against a real " +
                   "database. CI never skips: with CI=true or CI=1 these tests run, and fail " +
                   "without Docker.";
        }
    }
}

/// <summary>Decides whether the Docker-backed tests run: a daemon answers, or CI forbids skipping.</summary>
/// <remarks>
/// <para>
/// "Available" means the endpoint Testcontainers itself would use answers. The probe reads
/// <c>TestcontainersSettings.OS.DockerEndpointAuthConfig</c>, which Testcontainers resolves from
/// its own discovery — <c>DOCKER_HOST</c>, <c>~/.testcontainers.properties</c>, the
/// <c>TESTCONTAINERS_*</c> variables, the default sockets and pipes — and leaves null when no
/// candidate's daemon answers a ping. A socket file with no daemon behind it is therefore
/// unavailable (#173), and the probe cannot disagree with the container start it guards.
/// </para>
/// <para>
/// That discovery talks to the daemon, so it is not free: the probe runs once per process and is
/// cached, and its connection timeouts are paid once rather than per test. It never throws — any
/// exception, a <see cref="TypeInitializationException"/> from Testcontainers' static settings
/// included, reads as "unavailable".
/// </para>
/// </remarks>
internal static class DockerEndpoint
{
    private static readonly Lazy<bool> Probe = new(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets a value indicating whether the Docker endpoint Testcontainers would use answers.</summary>
    internal static bool IsAvailable => Probe.Value;

    /// <summary>
    /// Gets a value indicating whether the Docker-backed tests run in this process: Docker is
    /// available, or the <c>CI</c> environment variable forbids skipping them.
    /// </summary>
    internal static bool ShouldRun => ShouldRunFor(IsAvailable, Environment.GetEnvironmentVariable("CI"));

    /// <summary>
    /// The decision behind <see cref="ShouldRun"/>, free of any environment access: run when
    /// <paramref name="available"/>, or when <paramref name="ci"/> is <c>true</c> (any case) or
    /// <c>1</c>.
    /// </summary>
    internal static bool ShouldRunFor(bool available, string? ci) =>
        available
        || string.Equals(ci, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ci, "1", StringComparison.Ordinal);

    private static bool Detect()
    {
        try
        {
            return TestcontainersSettings.OS.DockerEndpointAuthConfig is not null;
        }
        catch (Exception)
        {
            // A probe that cannot answer must report "no Docker", never take the suite down.
            return false;
        }
    }
}
