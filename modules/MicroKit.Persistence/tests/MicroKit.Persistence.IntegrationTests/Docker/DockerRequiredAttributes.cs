using MicroKit.Persistence.Testing;
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
/// The attribute is xUnit's; the probe it asks is the framework-neutral <see cref="DockerProbe"/> of
/// MicroKit.Persistence.Testing (#197 phase 7), read at discovery time and cached for the run.
/// </para>
/// </remarks>
public sealed class DockerRequiredFactAttribute : FactAttribute
{
    public DockerRequiredFactAttribute()
    {
        if (!DockerProbe.IsAvailable)
            Skip = DockerRequired.SkipReason;
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
        if (!DockerProbe.IsAvailable)
            Skip = DockerRequired.SkipReason;
    }
}

internal static class DockerRequired
{
    internal const string SkipReason =
        "No Docker daemon answers — skipping the database suites (PostgreSQL, SQL Server). " +
        "Start Docker to run the tests against real databases.";
}
