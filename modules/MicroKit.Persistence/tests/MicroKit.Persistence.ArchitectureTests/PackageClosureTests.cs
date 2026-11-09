using System.Text.Json;

namespace MicroKit.Persistence.ArchitectureTests;

/// <summary>
/// #197 phase 7 (ADR-015): what each Testing-family package brings into a consumer, transitively. An
/// assembly's manifest lists only the references its IL uses; the dependency graph the build resolved —
/// this test project's <c>.deps.json</c>, which holds every referenced project with its dependencies — also
/// shows a package that is referenced but unused, and the edge to a project whose types go unused.
/// </summary>
public sealed class PackageClosureTests
{
    private static readonly string[] TestFrameworks =
        ["xunit", "nunit", "mstest", "microsoft.visualstudio.testplatform.testframework"];

    [Fact]
    public void Testing_BringsNoTestcontainers()
    {
        Closure("MicroKit.Persistence.Testing").Where(id => id.StartsWith("Testcontainers", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData("MicroKit.Persistence.Testing.PostgreSql")]
    [InlineData("MicroKit.Persistence.Testing.SqlServer")]
    public void TheFixturePackages_BringNoTestFramework(string project)
    {
        Closure(project).Where(id => TestFrameworks.Any(framework => id.StartsWith(framework, StringComparison.OrdinalIgnoreCase)))
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData("MicroKit.Persistence.Testing.PostgreSql", "Testcontainers.PostgreSql")]
    [InlineData("MicroKit.Persistence.Testing.SqlServer", "Testcontainers.MsSql")]
    public void TheFixturePackages_BringTesting_AndTheirTestcontainersModule_ButNoEntityFrameworkCore(string project, string module)
    {
        var closure = Closure(project);

        closure.ShouldContain("MicroKit.Persistence.Testing");
        closure.ShouldContain(module);
        closure.Where(id => id.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
    }

    [Fact]
    public void Closure_SeesATransitiveDependency()
    {
        // Testing → core → MicroKit.Result: the walk follows edges, not only the direct ones.
        Closure("MicroKit.Persistence.Testing").ShouldContain("MicroKit.Result");
    }

    // The ids every library the project's node depends on, transitively, in the resolved graph.
    private static HashSet<string> Closure(string project)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "MicroKit.Persistence.ArchitectureTests.deps.json")));
        var target = document.RootElement.GetProperty("targets").EnumerateObject().Single().Value;
        var libraries = target.EnumerateObject().ToDictionary(
            library => library.Name[..library.Name.IndexOf('/', StringComparison.Ordinal)],
            library => library.Value,
            StringComparer.OrdinalIgnoreCase);

        libraries.ContainsKey(project).ShouldBeTrue($"{project} is not in the resolved graph");
        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([project]);
        while (pending.TryPop(out var id))
        {
            if (!libraries.TryGetValue(id, out var library) || !library.TryGetProperty("dependencies", out var dependencies))
                continue;
            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (closure.Add(dependency.Name))
                    pending.Push(dependency.Name);
            }
        }

        return closure;
    }
}
