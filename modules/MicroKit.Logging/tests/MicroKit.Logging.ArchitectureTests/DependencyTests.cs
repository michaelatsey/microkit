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
        Referenced(Abstractions).Should().NotContain("MicroKit.Logging",
            because: "Abstractions must have no dependency on the Core implementation");

    [Fact]
    public void Abstractions_DoesNotReferenceAspNetCore() =>
        Referenced(Abstractions).Should().NotContain("MicroKit.Logging.AspNetCore",
            because: "Abstractions must be provider-independent");

    [Fact]
    public void Abstractions_DoesNotReferenceOpenTelemetry() =>
        Referenced(Abstractions).Should().NotContain("MicroKit.Logging.OpenTelemetry",
            because: "Abstractions must be provider-independent");

    [Fact]
    public void Abstractions_DoesNotReferenceDiagnostics() =>
        Referenced(Abstractions).Should().NotContain("MicroKit.Logging.Diagnostics",
            because: "Abstractions must be provider-independent");

    // ── Layer 1: Core ──────────────────────────────────────────────────────

    [Fact]
    public void Core_DoesNotReferenceAspNetCore() =>
        Referenced(Core).Should().NotContain("MicroKit.Logging.AspNetCore",
            because: "Core must not pull in provider-specific packages");

    [Fact]
    public void Core_DoesNotReferenceOpenTelemetry() =>
        Referenced(Core).Should().NotContain("MicroKit.Logging.OpenTelemetry",
            because: "Core must not pull in provider-specific packages");

    [Fact]
    public void Core_DoesNotReferenceDiagnostics() =>
        Referenced(Core).Should().NotContain("MicroKit.Logging.Diagnostics",
            because: "Core must not pull in provider-specific packages");

    // ── Layer 2: Cross-provider isolation ──────────────────────────────────

    [Fact]
    public void AspNetCore_DoesNotReferenceOpenTelemetry() =>
        Referenced(AspNetCore).Should().NotContain("MicroKit.Logging.OpenTelemetry",
            because: "providers must not cross-reference each other");

    [Fact]
    public void AspNetCore_DoesNotReferenceDiagnostics() =>
        Referenced(AspNetCore).Should().NotContain("MicroKit.Logging.Diagnostics",
            because: "providers must not cross-reference each other");

    [Fact]
    public void OpenTelemetry_DoesNotReferenceAspNetCore() =>
        Referenced(OpenTelemetry).Should().NotContain("MicroKit.Logging.AspNetCore",
            because: "providers must not cross-reference each other");

    [Fact]
    public void OpenTelemetry_DoesNotReferenceDiagnostics() =>
        Referenced(OpenTelemetry).Should().NotContain("MicroKit.Logging.Diagnostics",
            because: "providers must not cross-reference each other");

    [Fact]
    public void Diagnostics_DoesNotReferenceAspNetCore() =>
        Referenced(Diagnostics).Should().NotContain("MicroKit.Logging.AspNetCore",
            because: "providers must not cross-reference each other");

    [Fact]
    public void Diagnostics_DoesNotReferenceOpenTelemetry() =>
        Referenced(Diagnostics).Should().NotContain("MicroKit.Logging.OpenTelemetry",
            because: "providers must not cross-reference each other");
}
