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

    [Fact]
    public void EverySourceFile_DeclaresATest()
    {
        // .claude/rules/testing.md: helper types (stubs of aggregates, identifiers) are defined in the
        // same file as the tests that use them, never in a file of their own.
        var root = Path.Combine(AppContext.BaseDirectory, "test-sources");

        var files = File.ReadAllLines(Path.Combine(root, "files.txt"))
            // Only this project's own sources: not generated files under obj/, nor the test SDK's
            // entry point, which is compiled from the package cache by absolute path.
            .Where(f => !Path.IsPathRooted(f) && !f.StartsWith("obj/", StringComparison.Ordinal) && f.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => Path.Combine(root, f))
            .ToArray();
        var offenders = files
            .Where(f => Path.GetFileName(f) != "GlobalUsings.cs")
            .Where(f => File.ReadAllText(f) is var text && !text.Contains("[Fact]", StringComparison.Ordinal) && !text.Contains("[Theory]", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f))
            .ToArray();

        files.ShouldContain(f => Path.GetFileName(f) == "AuditRulesTests.cs");
        offenders.ShouldBeEmpty();
    }

    private static string StripArity(string name) =>
        name.IndexOf('`', StringComparison.Ordinal) is var tick and >= 0 ? name[..tick] : name;

    private static bool IsInterfaceName(string name) =>
        name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]);
}
#pragma warning restore IL2026, IL2070, IL2075
