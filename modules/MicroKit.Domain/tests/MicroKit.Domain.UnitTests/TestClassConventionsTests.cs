using MicroKit.Domain.Aggregates;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests;

// Enforces .claude/rules/testing.md: a test class is sealed and named `[Type]Tests`, after a type of
// MicroKit.Domain. This class names a convention, not a type, so it exempts itself. The test
// assemblies are never trimmed.
#pragma warning disable IL2026, IL2070, IL2075
public sealed class TestClassConventionsTests
{
    [Fact]
    public void EveryTestClass_IsSealedAndNamedAfterADomainType()
    {
        var domainTypes = typeof(Entity<>).Assembly.GetTypes()
            .Select(t => StripArity(t.Name))
            .SelectMany(name => IsInterfaceName(name) ? [name, name[1..]] : new[] { name })
            .ToHashSet(StringComparer.Ordinal);

        var offenders = typeof(TestClassConventionsTests).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsNested && t != typeof(TestClassConventionsTests))
            .Where(t => t.GetMethods().Any(m => m.IsDefined(typeof(FactAttribute), inherit: true)))
            .Where(t => !t.IsSealed
                        || !t.Name.EndsWith("Tests", StringComparison.Ordinal)
                        || !domainTypes.Contains(t.Name[..^"Tests".Length]))
            .Select(t => t.FullName)
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    private static string StripArity(string name) =>
        name.IndexOf('`', StringComparison.Ordinal) is var tick and >= 0 ? name[..tick] : name;

    private static bool IsInterfaceName(string name) =>
        name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]);
}
#pragma warning restore IL2026, IL2070, IL2075
