namespace MicroKit.Logging.ArchitectureTests;

public sealed class DependencyTests
{
    private static readonly Assembly Abstractions = typeof(LogPropertyNames).Assembly;
    private static readonly Assembly Core = typeof(MicroKitLoggingOptions).Assembly;
    private static readonly Assembly AspNetCore = typeof(AspNetCoreLoggingOptions).Assembly;
    private static readonly Assembly OpenTelemetry = typeof(MicroKitLogProcessor).Assembly;
    private static readonly Assembly Diagnostics = typeof(ActivitySources).Assembly;

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "Architecture tests run in a non-trimmed test host; assembly references are always present.")]
    private static IEnumerable<string> Referenced(Assembly a) =>
        a.GetReferencedAssemblies().Select(n => n.Name ?? string.Empty);

    // ── Layer 0: Abstractions ──────────────────────────────────────────────

    [Fact]
    public void Abstractions_DoesNotReferenceCore() =>
        Referenced(Abstractions).ShouldNotContain("MicroKit.Logging",
            "Abstractions must have no dependency on the Core implementation");

    [Fact]
    public void Abstractions_DoesNotReferenceAspNetCore() =>
        Referenced(Abstractions).ShouldNotContain("MicroKit.Logging.AspNetCore",
            "Abstractions must be provider-independent");

    [Fact]
    public void Abstractions_DoesNotReferenceOpenTelemetry() =>
        Referenced(Abstractions).ShouldNotContain("MicroKit.Logging.OpenTelemetry",
            "Abstractions must be provider-independent");

    [Fact]
    public void Abstractions_DoesNotReferenceDiagnostics() =>
        Referenced(Abstractions).ShouldNotContain("MicroKit.Logging.Diagnostics",
            "Abstractions must be provider-independent");

    // ── Layer 1: Core ──────────────────────────────────────────────────────

    [Fact]
    public void Core_DoesNotReferenceAspNetCore() =>
        Referenced(Core).ShouldNotContain("MicroKit.Logging.AspNetCore",
            "Core must not pull in provider-specific packages");

    [Fact]
    public void Core_DoesNotReferenceOpenTelemetry() =>
        Referenced(Core).ShouldNotContain("MicroKit.Logging.OpenTelemetry",
            "Core must not pull in provider-specific packages");

    [Fact]
    public void Core_DoesNotReferenceDiagnostics() =>
        Referenced(Core).ShouldNotContain("MicroKit.Logging.Diagnostics",
            "Core must not pull in provider-specific packages");

    // ── Layer 2: Cross-provider isolation ──────────────────────────────────

    [Fact]
    public void AspNetCore_DoesNotReferenceOpenTelemetry() =>
        Referenced(AspNetCore).ShouldNotContain("MicroKit.Logging.OpenTelemetry",
            "providers must not cross-reference each other");

    [Fact]
    public void AspNetCore_DoesNotReferenceDiagnostics() =>
        Referenced(AspNetCore).ShouldNotContain("MicroKit.Logging.Diagnostics",
            "providers must not cross-reference each other");

    [Fact]
    public void OpenTelemetry_DoesNotReferenceAspNetCore() =>
        Referenced(OpenTelemetry).ShouldNotContain("MicroKit.Logging.AspNetCore",
            "providers must not cross-reference each other");

    [Fact]
    public void OpenTelemetry_DoesNotReferenceDiagnostics() =>
        Referenced(OpenTelemetry).ShouldNotContain("MicroKit.Logging.Diagnostics",
            "providers must not cross-reference each other");

    [Fact]
    public void Diagnostics_DoesNotReferenceAspNetCore() =>
        Referenced(Diagnostics).ShouldNotContain("MicroKit.Logging.AspNetCore",
            "providers must not cross-reference each other");

    [Fact]
    public void Diagnostics_DoesNotReferenceOpenTelemetry() =>
        Referenced(Diagnostics).ShouldNotContain("MicroKit.Logging.OpenTelemetry",
            "providers must not cross-reference each other");
}
