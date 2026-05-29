using System.Reflection;
using MediatR;
using MicroKit.MediatR.Behaviors;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.ArchitectureTests;

/// <summary>
/// Enforces ADR-002 (BehaviorBase mandatory inheritance) and the sealed-class rule for all
/// pipeline behavior implementations in <c>MicroKit.MediatR.Behaviors</c>.
/// </summary>
public sealed class BehaviorRulesTests
{
    private static readonly Assembly BehaviorsAssembly = typeof(LoggingBehavior<,>).Assembly;
    private static readonly Assembly CoreAssembly = typeof(BehaviorBase<,>).Assembly;

    private static string FailingNames(TestResult result) =>
        result.FailingTypeNames is { } names ? string.Join(", ", names) : string.Empty;

    /// <summary>
    /// ADR-002: every concrete <c>IPipelineBehavior&lt;,&gt;</c> implementation must
    /// inherit <c>BehaviorBase&lt;TRequest, TResponse&gt;</c>.
    /// Direct <c>IPipelineBehavior</c> implementations bypass Order enforcement and the
    /// <c>Result&lt;T&gt;</c> failure-construction helpers.
    /// </summary>
    [Fact]
    public void AllPipelineBehaviors_InheritBehaviorBase()
    {
        var pipelineBehaviorDef = typeof(IPipelineBehavior<,>);
        var behaviorBaseDef = typeof(BehaviorBase<,>);

        var violators = BehaviorsAssembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && !t.IsInterface)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == pipelineBehaviorDef))
            .Where(t => !InheritsFromOpenGeneric(t, behaviorBaseDef))
            .Select(t => t.FullName)
            .ToList();

        violators.ShouldBeEmpty(
            $"The following types implement IPipelineBehavior<,> but do not inherit BehaviorBase<,> (ADR-002): " +
            $"{string.Join(", ", violators)}");
    }

    /// <summary>All concrete pipeline behavior implementations must be sealed.</summary>
    [Fact]
    public void AllPipelineBehaviors_AreSealed()
    {
        var pipelineBehaviorDef = typeof(IPipelineBehavior<,>);

        var unsealed = BehaviorsAssembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && !t.IsInterface)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == pipelineBehaviorDef))
            .Where(t => !t.IsSealed)
            .Select(t => t.FullName)
            .ToList();

        unsealed.ShouldBeEmpty(
            $"All pipeline behavior implementations must be sealed: {string.Join(", ", unsealed)}");
    }

    /// <summary>
    /// <c>BehaviorBase&lt;,&gt;</c> must live in the Core assembly, not in Abstractions or Behaviors.
    /// Placing it elsewhere would break the dependency graph (Abstractions would gain a Core dependency,
    /// or Behaviors would own a shared base class that Core consumers also need).
    /// </summary>
    [Fact]
    public void BehaviorBase_IsDefinedInCore_NotAbstractions()
    {
        typeof(BehaviorBase<,>).Assembly.ShouldBe(CoreAssembly,
            "BehaviorBase<,> must be defined in MicroKit.MediatR (core), not in Abstractions or Behaviors");
    }

    /// <summary>All public error types in the Behaviors assembly must be sealed records.</summary>
    [Fact]
    public void BehaviorErrors_AreSealed()
    {
        var result = Types
            .InAssembly(BehaviorsAssembly)
            .That().ArePublic()
            .And().HaveNameEndingWith("Error")
            .Should().BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"All behavior error types must be sealed. Violators: {FailingNames(result)}");
    }

    /// <summary>
    /// No type in Behaviors may inject <c>IMediator</c> directly.
    /// Behaviors interact with the pipeline through the <c>RequestHandlerDelegate</c>
    /// delegate; direct IMediator injection would risk dispatch loops.
    /// </summary>
    [Fact]
    public void NoBehavior_InjectsMediatRIMediator_Directly()
    {
        var mediatRType = typeof(IMediator);

        var violators = BehaviorsAssembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(ctor => ctor.GetParameters().Any(p => p.ParameterType == mediatRType)))
            .Select(t => t.FullName)
            .ToList();

        violators.ShouldBeEmpty(
            $"No behavior may inject IMediator directly (would risk dispatch loops). Violators: {string.Join(", ", violators)}");
    }

    /// <summary>
    /// The Behaviors assembly must not reference Microsoft.EntityFrameworkCore.
    /// Behaviors operate at the pipeline level — persistence concerns belong in handlers.
    /// </summary>
    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Architecture tests run in a non-trimmed test host.")]
    public void Behaviors_DoesNotReferenceEntityFramework() =>
        BehaviorsAssembly.GetReferencedAssemblies()
            .Select(n => n.Name ?? string.Empty)
            .ShouldNotContain("Microsoft.EntityFrameworkCore",
                "Behaviors must not reference EntityFrameworkCore — persistence belongs in handlers");

    private static bool InheritsFromOpenGeneric(Type type, Type openGenericBase)
    {
        var cursor = type.BaseType;
        while (cursor is not null && cursor != typeof(object))
        {
            if (cursor.IsGenericType && cursor.GetGenericTypeDefinition() == openGenericBase)
                return true;
            cursor = cursor.BaseType;
        }
        return false;
    }
}
