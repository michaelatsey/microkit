using System.Reflection;
using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.ValueObjects;
using Mono.Cecil;
using NetArchTest.Rules;
using Shouldly;

namespace MicroKit.Domain.ArchitectureTests;

public sealed class DomainArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Entity<>).Assembly;

    private static readonly string[] ForbiddenFrameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.DependencyInjection",
    ];

    [Fact]
    public void Domain_ReferencesNoForbiddenAssembly()
    {
        using var module = ModuleDefinition.ReadModule(DomainAssembly.Location);

        var references = module.AssemblyReferences.Select(a => a.Name).ToArray();
        var forbidden = references
            .Where(name => name.StartsWith("MicroKit.", StringComparison.Ordinal)
                           || ForbiddenFrameworks.Any(f => name.StartsWith(f, StringComparison.Ordinal)))
            .ToArray();

        references.ShouldContain("System.Runtime");
        forbidden.ShouldBeEmpty();
    }

    [Fact]
    public void Domain_TypesDependOnNoForbiddenFramework()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenFrameworks)
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Domain_DeclaresNoPersistenceContract()
    {
        // MicroKit.Persistence.Abstractions owns repositories and the unit of work (ADR-003 D1).
        // The names are read with Cecil, never resolved to runtime types: a coverage collector
        // injects types into the instrumented assembly that reflection cannot resolve.
        using var module = ModuleDefinition.ReadModule(DomainAssembly.Location);
        var names = module.GetTypes().Select(t => t.FullName).ToArray();

        names.ShouldContain(typeof(Entity<>).FullName!);
        names
            .Where(name => name.StartsWith("MicroKit.Domain.Repositories", StringComparison.Ordinal)
                           || name.Contains(".IRepository", StringComparison.Ordinal)
                           || name.Contains(".IReadRepository", StringComparison.Ordinal)
                           || name.Contains(".IUnitOfWork", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    [Fact]
    public void ValueObjects_AreSealed()
    {
        var result = ValueObjectsAreSealed(DomainAssembly);

        result.FailingTypeNames.ShouldBeNull();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void DomainEvents_AreRecords()
    {
        var result = DomainEventsAreRecords(DomainAssembly);

        result.FailingTypeNames.ShouldBeNull();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void DomainEvents_RuleCoversTheDomainEventBase()
    {
        Types.InAssembly(DomainAssembly)
            .That().ImplementInterface(typeof(IDomainEvent))
            .GetTypes()
            .ShouldContain(t => t.FullName == typeof(DomainEvent).FullName);
    }

    // The two tests below run each rule against this test assembly, whose fixtures break it on
    // purpose, to prove the rule can fail.

    [Fact]
    public void ValueObjectRule_RejectsAnUnsealedValueObject()
    {
        var result = ValueObjectsAreSealed(typeof(DomainArchitectureTests).Assembly);

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldBe([typeof(UnsealedValueObject).FullName]);
    }

    [Fact]
    public void DomainEventRule_RejectsANonRecordEvent()
    {
        var result = DomainEventsAreRecords(typeof(DomainArchitectureTests).Assembly);

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldBe([typeof(ClassEvent).FullName]);
    }

    private static TestResult ValueObjectsAreSealed(Assembly assembly) =>
        Types.InAssembly(assembly)
            .That().ImplementInterface(typeof(IValueObject))
            .Should().BeSealed()
            .GetResult();

    private static TestResult DomainEventsAreRecords(Assembly assembly) =>
        Types.InAssembly(assembly)
            .That().ImplementInterface(typeof(IDomainEvent))
            .Should().MeetCustomRule(new IsRecordRule())
            .GetResult();

    private sealed class IsRecordRule : ICustomRule
    {
        // The compiler emits an unspeakable <Clone>$ method on every record class, and a
        // compiler-generated PrintMembers on every record struct.
        public bool MeetsRule(TypeDefinition type) =>
            type.Methods.Any(m => m.Name == "<Clone>$")
            || (type.IsValueType && type.Methods.Any(m =>
                m.Name == "PrintMembers"
                && m.CustomAttributes.Any(a => a.AttributeType.FullName == typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute).FullName)));
    }
}

public sealed record SealedValueObject(int Value) : IValueObject;

public readonly record struct StructValueObject(int Value) : IValueObject;

#pragma warning disable CA1852 // deliberately unsealed: the fixture the sealed rule must reject
public class UnsealedValueObject : IValueObject;
#pragma warning restore CA1852

public sealed record RecordEvent(DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public readonly record struct RecordStructEvent(Guid EventId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class ClassEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UnixEpoch;
}
