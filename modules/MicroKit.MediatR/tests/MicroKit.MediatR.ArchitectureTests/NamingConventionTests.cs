using System.Reflection;
using MediatR;
using MicroKit.MediatR.Behaviors;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.ArchitectureTests;

/// <summary>
/// Verifies naming conventions across all three MicroKit.MediatR assemblies:
/// extension classes are static, behavior types implement the pipeline contract,
/// and marker interfaces are confined to Abstractions.
/// </summary>
public sealed class NamingConventionTests
{
    private static readonly Assembly Abstractions = typeof(ICacheableQuery).Assembly;
    private static readonly Assembly Core = typeof(PipelineOrder).Assembly;
    private static readonly Assembly Behaviors = typeof(LoggingBehavior<,>).Assembly;

    private static string FailingNames(TestResult result) =>
        result.FailingTypeNames is { } names ? string.Join(", ", names) : string.Empty;

    /// <summary>
    /// All public types in the Abstractions assembly must be in the <c>MicroKit.MediatR</c>
    /// root namespace (not <c>MicroKit.MediatR.Abstractions</c> or any sub-namespace).
    /// This keeps the consuming API surface clean: <c>using MicroKit.MediatR;</c> is the
    /// only import a consumer needs.
    /// </summary>
    [Fact]
    public void AbstractionsPublicTypes_AreInMicroKitMediatRNamespace()
    {
        var result = Types
            .InAssembly(Abstractions)
            .That().ArePublic()
            .Should().ResideInNamespace("MicroKit.MediatR")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"All public types in Abstractions must reside in 'MicroKit.MediatR'. Violators: {FailingNames(result)}");
    }

    /// <summary>
    /// All public types in the Core assembly must be in the <c>MicroKit.MediatR</c>
    /// root namespace, keeping the public API consistent with Abstractions.
    /// </summary>
    [Fact]
    public void CorePublicTypes_AreInMicroKitMediatRNamespace()
    {
        var result = Types
            .InAssembly(Core)
            .That().ArePublic()
            .Should().ResideInNamespace("MicroKit.MediatR")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"All public types in Core must reside in 'MicroKit.MediatR'. Violators: {FailingNames(result)}");
    }

    /// <summary>All public types ending with "Extensions" must be static classes.</summary>
    [Fact]
    public void TypesEndingWithExtensions_AreStatic()
    {
        var allAssemblies = new[] { Abstractions, Core, Behaviors };

        var result = Types
            .InAssemblies(allAssemblies)
            .That().ArePublic()
            .And().HaveNameEndingWith("Extensions")
            .Should().BeStatic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"All *Extensions types must be static. Violators: {FailingNames(result)}");
    }

    /// <summary>
    /// Every public type whose name ends with "Behavior" must implement
    /// <c>IPipelineBehavior&lt;,&gt;</c> — naming implies pipeline membership.
    /// </summary>
    [Fact]
    public void TypesEndingWithBehavior_ImplementIPipelineBehavior()
    {
        var pipelineBehaviorDef = typeof(IPipelineBehavior<,>);

        var violators = Behaviors.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && !t.IsInterface)
            .Where(t => t.Name.EndsWith("Behavior", StringComparison.Ordinal))
            .Where(t => !t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == pipelineBehaviorDef))
            .Select(t => t.FullName)
            .ToList();

        violators.ShouldBeEmpty(
            $"Types ending with 'Behavior' must implement IPipelineBehavior<,>: {string.Join(", ", violators)}");
    }

    /// <summary>
    /// <c>PipelineOrder</c> (the canonical order registry) must be defined in Core, not
    /// in Abstractions (it references behavior types) or Behaviors (it's a shared contract).
    /// </summary>
    [Fact]
    public void PipelineOrder_IsDefinedInCore()
    {
        typeof(PipelineOrder).Assembly.ShouldBe(Core,
            "PipelineOrder must live in MicroKit.MediatR (core) as the canonical order registry");
    }

    /// <summary>
    /// Behavior marker interfaces (<c>ICacheableQuery</c>, <c>IIdempotentCommand</c>, etc.)
    /// must be defined in Abstractions, not in Core or Behaviors.
    /// This ensures consumers of Abstractions alone can declare opt-in contracts without
    /// taking a dependency on the behavior implementations.
    /// </summary>
    [Fact]
    public void BehaviorMarkerInterfaces_AreDefinedInAbstractions()
    {
        var markers = new[]
        {
            typeof(ICacheableQuery),
            typeof(IIdempotentCommand),
            typeof(IRetryableRequest),
            typeof(IAuthorizedRequest),
        };

        foreach (var marker in markers)
        {
            marker.Assembly.ShouldBe(Abstractions,
                $"{marker.Name} must be defined in MicroKit.MediatR.Abstractions");
        }
    }
}
