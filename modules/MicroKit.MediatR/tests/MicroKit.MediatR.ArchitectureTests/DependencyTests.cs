using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MicroKit.MediatR.Behaviors;
using MicroKit.MediatR.Testing;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.ArchitectureTests;

/// <summary>
/// Verifies that the 4-layer assembly dependency graph is intact.
/// Abstractions ← Core ← Behaviors: no layer may reference one above it in the graph.
/// Sibling isolation: Behaviors and Testing must not reference each other.
/// Cross-module guard: Behaviors must not pull in higher-level MicroKit modules.
/// </summary>
public sealed class DependencyTests
{
    // Assembly anchors — one unambiguous public type per layer.
    private static readonly Assembly Abstractions = typeof(ICacheableQuery).Assembly;
    private static readonly Assembly Core = typeof(PipelineOrder).Assembly;
    private static readonly Assembly Behaviors = typeof(LoggingBehavior<,>).Assembly;
    private static readonly Assembly Testing = typeof(CommandHandlerTestHarness<,>).Assembly;

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Architecture tests run in a non-trimmed test host; referenced assemblies are always present.")]
    private static IEnumerable<string> Referenced(Assembly a) =>
        a.GetReferencedAssemblies().Select(n => n.Name ?? string.Empty);

    // ── Layer 0: Abstractions ──────────────────────────────────────────────

    [Fact]
    public void Abstractions_DoesNotReferenceCore() =>
        Referenced(Abstractions).ShouldNotContain("MicroKit.MediatR",
            "Abstractions must not depend on the Core implementation");

    [Fact]
    public void Abstractions_DoesNotReferenceBehaviors() =>
        Referenced(Abstractions).ShouldNotContain("MicroKit.MediatR.Behaviors",
            "Abstractions must not depend on the Behaviors layer");

    // ── Layer 1: Core ──────────────────────────────────────────────────────

    [Fact]
    public void Core_DoesNotReferenceBehaviors() =>
        Referenced(Core).ShouldNotContain("MicroKit.MediatR.Behaviors",
            "Core must not depend on the Behaviors layer — dependency only flows downward");

    // ── Sibling isolation ──────────────────────────────────────────────────

    [Fact]
    public void Behaviors_DoesNotReferenceTesting() =>
        Referenced(Behaviors).ShouldNotContain("MicroKit.MediatR.Testing",
            "Behaviors and Testing are siblings — neither may reference the other");

    [Fact]
    public void Testing_DoesNotReferenceBehaviors() =>
        Referenced(Testing).ShouldNotContain("MicroKit.MediatR.Behaviors",
            "Behaviors and Testing are siblings — neither may reference the other");

    // ── Cross-module guard (Level 2 must not reference Level 3+) ──────────

    [Fact]
    public void Behaviors_DoesNotReferencePersistenceModule() =>
        Referenced(Behaviors).ShouldNotContain("MicroKit.Persistence",
            "MicroKit.MediatR is Level 2 and must not reference Level 3+ modules");

    [Fact]
    public void Behaviors_DoesNotReferenceMessagingModule() =>
        Referenced(Behaviors).ShouldNotContain("MicroKit.Messaging",
            "MicroKit.MediatR is Level 2 and must not reference Level 3+ modules");
}
