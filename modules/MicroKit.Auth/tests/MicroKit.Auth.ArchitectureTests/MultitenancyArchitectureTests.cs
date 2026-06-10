using System.Reflection;

namespace MicroKit.Auth.ArchitectureTests;

public sealed class MultitenancyArchitectureTests
{
    private static readonly Assembly MultitenancyAssembly = typeof(AuthTenantResolutionStrategy).Assembly;

    [Fact]
    public void Multitenancy_ShouldNotDependOn_AuthCore()
    {
        // Multitenancy depends on Abstractions + MicroKit.Multitenancy.Abstractions only — never Core
        var coreAssemblyName = typeof(CurrentUser).Assembly.GetName().Name;
        var referencedNames = MultitenancyAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referencedNames.ShouldNotContain(coreAssemblyName);
    }

    [Fact]
    public void Multitenancy_ShouldNotDependOn_AspNetCore()
    {
        var aspNetCoreAssemblyName = typeof(CurrentUserMiddleware).Assembly.GetName().Name;
        var referencedNames = MultitenancyAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referencedNames.ShouldNotContain(aspNetCoreAssemblyName);
    }

    [Fact]
    public void Multitenancy_ShouldNotDependOn_Jwt()
    {
        var jwtAssemblyName = typeof(JwtValidator).Assembly.GetName().Name;
        var referencedNames = MultitenancyAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referencedNames.ShouldNotContain(jwtAssemblyName);
    }
}
