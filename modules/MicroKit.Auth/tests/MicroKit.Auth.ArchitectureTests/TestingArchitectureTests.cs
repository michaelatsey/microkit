using System.Reflection;

namespace MicroKit.Auth.ArchitectureTests;

public sealed class TestingArchitectureTests
{
    private static readonly Assembly TestingAssembly = typeof(FakeCurrentUser).Assembly;

    [Fact]
    public void Testing_ShouldNotDependOn_AuthCore()
    {
        // Testing depends on Abstractions + Permissions only — never Core or any framework package
        var coreAssemblyName = typeof(CurrentUser).Assembly.GetName().Name;
        var referencedNames = TestingAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referencedNames.ShouldNotContain(coreAssemblyName);
    }

    [Fact]
    public void Testing_ShouldNotDependOn_AspNetCore()
    {
        var aspNetCoreAssemblyName = typeof(CurrentUserMiddleware).Assembly.GetName().Name;
        var referencedNames = TestingAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referencedNames.ShouldNotContain(aspNetCoreAssemblyName);
    }

    [Fact]
    public void Testing_ShouldNotDependOn_Jwt()
    {
        var jwtAssemblyName = typeof(JwtValidator).Assembly.GetName().Name;
        var referencedNames = TestingAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referencedNames.ShouldNotContain(jwtAssemblyName);
    }

    [Fact]
    public void Testing_ShouldNotDependOn_Roles()
    {
        var rolesAssemblyName = typeof(RoleRegistry).Assembly.GetName().Name;
        var referencedNames = TestingAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referencedNames.ShouldNotContain(rolesAssemblyName);
    }
}
